// ******************************************************************
// Copyright © 2015-2026 Uno Platform inc. All rights reserved.
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//      http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
//
// ******************************************************************

using System;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.Build.Framework;
using Uno.Wasm.Bootstrap.AOTWrapperSeeding;

namespace Uno.Wasm.Bootstrap;

/// <summary>
/// Writes the AOT wrapper seeds for a profile-guided AOT compilation: a seed assembly whose seed methods call uncompiled methods of the interface,
/// virtual and delegate target signatures of the AOT inputs that compiled call sites may not name (see <see cref="SignatureCollector"/>), and an AOT
/// profile that lists the seed methods. Compiling the seed methods makes the AOT compiler emit the interpreter entry wrappers that methods missing
/// from the app's profile need. The targets run it as <see cref="GenerateAOTWrapperSeedsTask_v0"/>.
/// </summary>
public class GenerateAOTWrapperSeedsTask : Microsoft.Build.Utilities.Task, ICancelableTask
{
	private readonly CancellationTokenSource _cancellation = new();

	/// <summary>The assemblies about to be AOT-compiled.</summary>
	[Required]
	public ITaskItem[] Assemblies { get; set; } = [];

	/// <summary>The seed assembly to write, whose file name is its assembly name.</summary>
	[Required]
	public string SeedAssembly { get; set; } = "";

	/// <summary>The AOT profile to write, which lists the seed methods.</summary>
	[Required]
	public string SeedProfile { get; set; } = "";

	/// <summary>The expected wrappers file to write, which <see cref="ValidateAOTWrapperSeedsTask"/> reads.</summary>
	[Required]
	public string ExpectedWrappers { get; set; } = "";

	/// <summary>The page the build messages point to.</summary>
	[Required]
	public string DocumentationUrl { get; set; } = "";

	/// <summary>The page the build messages ask to report problems on.</summary>
	[Required]
	public string IssuesUrl { get; set; } = "";

	/// <summary>Whether the files were written. Files written by an earlier build are deleted when they couldn't be.</summary>
	[Output]
	public bool Generated { get; set; }

	public void Cancel()
	{
		try
		{
			_cancellation.Cancel();
		}
		catch (ObjectDisposedException)
		{
			// The generation has finished.
		}
	}

	public override bool Execute()
	{
		try
		{
			return Generate();
		}
		finally
		{
			_cancellation.Dispose();
		}
	}

	private bool Generate()
	{
		AotWrapperSeedGenerator.Result result;
		var files = new AotWrapperSeedGenerator.SeedFiles(SeedAssembly, SeedProfile, ExpectedWrappers);

		try
		{
			var assemblies = Assemblies.Select(a => a.GetMetadata("FullPath"));
			result = AotWrapperSeedGenerator.Generate(assemblies, files, cancellationToken: _cancellation.Token);
		}
		catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
		{
			// The files are only written once the seeds are collected, so they're those of the last generation, which the stamp tells about.
			return false;
		}
		catch (Exception e)
		{
			DeleteFiles(files);

			// The seeding only adds safety, so a failure leaves the build as it would be without it. The first sentence is also the one of
			// _UnoCheckAOTWrapperSeedingRan's UNOWASM008, in the targets.
			LogWarning("UNOWASM008",
				"[Uno] Couldn't add the crash prevention to this profile-guided AOT build, so the app can still crash as described in " +
				$"{DocumentationUrl}. Please report this warning at {IssuesUrl}, with the stack trace from a detailed build log (-v:detailed) or a " +
				$"binary log (-bl): {e.GetType().Name}: {e.Message}");

			Log.LogMessage(MessageImportance.Low, $"[Uno] AOT wrapper seeding failed: {e}");
			return true;
		}

		Generated = true;

		var collected = result.Collected;
		Log.LogMessage(MessageImportance.Normal,
			$"[Uno] AOT wrapper seeding: {result.SeedCount} signatures seeded in {result.SeedMethodCount} methods, {result.GsharedvtInWrapperCount} " +
			$"gsharedvt_in_sig and {result.InterpInWrapperCount} interp_in wrappers expected, from {result.AssemblyCount} assemblies " +
			$"({collected.InterfaceMethods} interface methods, {collected.VirtualMethods} virtual methods, {collected.DelegateTargets} delegate " +
			$"targets, {collected.ConstructedTypes} generic instances; {collected.Unsupported} unsupported signatures, {result.Skipped.Count} items " +
			$"skipped, {result.Unchecked.Count} seeds unchecked, {result.UndeterminedMethods.Count} methods not checked for UNOWASM010) in " +
			$"{result.Elapsed.TotalMilliseconds:0} ms.");

		foreach (string skipped in result.Skipped)
		{
			Log.LogMessage(MessageImportance.Normal, $"[Uno] AOT wrapper seeding skipped {skipped}");
		}

		foreach (string uncheckedSeed in result.Unchecked)
		{
			Log.LogMessage(MessageImportance.Normal, $"[Uno] AOT wrapper seeding: the check doesn't look for the wrapper of {uncheckedSeed}");
		}

		foreach (var undetermined in result.UndeterminedMethods)
		{
			Log.LogMessage(MessageImportance.Normal, $"[Uno] AOT wrapper seeding couldn't check {undetermined.Member} for UNOWASM010: {undetermined.Reason}");
		}

		// Compiled code that calls such a method while it's interpreted can't find its wrapper: above 8 parameters, the runtime aborts as soon as it
		// resolves the method, when the code that calls it directly first runs, or when it builds an IMT slot the method is in (only virtual instance
		// methods are), with interp_create_method_pointer_llvmonly in interp.c; below, the call fails when it's made (no_gsharedvt_in_wrapper in
		// mini-runtime.c). Listing the method in the profile doesn't make it safe: once compiled, the runtime can't find its gsharedvt_out_sig wrapper
		// either, so interpreted code that calls it aborts (init_jit_call_info in interp.c).
		foreach (var finding in result.UnenterableMethods)
		{
			string crash = finding.ParameterCount <= WrapperSignature.MaxInterpEntryArgs
				? $"Compiled code that calls {finding.Method} can crash the app when it makes the call"
				: $"Compiled code that calls {finding.Method} can crash the app when it starts running, even if it doesn't make the call"
					+ (finding.IsVirtual ? ", and so can compiled code that calls another method of its class through an interface" : "");

			LogWarning("UNOWASM010",
				$"[Uno] {crash}: {DescribeCause(finding)}. {DescribeType(finding)} Listing the method in the AOT profile only moves the crash to the " +
				"interpreted code that calls it. Remove the default value or the attribute, or pass the parameter with 'in'. If you can't change the " +
				$"method, report the warning to the library's authors. To remove the warning, add UNOWASM010 to NoWarn. See {DocumentationUrl}");
		}

		// One warning per struct that the runtime can't map (WrapperSignature.FindUnsharableStruct), at the root of a cycle or with too many type
		// arguments. Compiled calls to such methods that can enter the interpreter make the AOT compiler overflow its stack or assert (calls.c adds
		// their signatures to interp_in_signatures), so recording them in the profile doesn't help.
		foreach (var unprotected in result.UnprotectedStructs)
		{
			var (cause, remedy) = unprotected.Reason == WrapperSignature.UnsharableReason.CyclicLayout
				? ($"{unprotected.Type} contains itself through a field such as a ReadOnlyMemory<T> or an ArraySegment<T> of its own type",
					"hold its items in a class or an array")
				: ($"{unprotected.Type} has more than 15 type arguments", "give it at most 15 type arguments");

			LogWarning("UNOWASM010",
				$"[Uno] The app may crash when compiled code calls a method that takes or returns {unprotected.Type}, or a struct that holds it, by " +
				$"value, such as {unprotected.Method}, or calls another method of its class through an interface: {cause}, for which the runtime " +
				"can't make the code that enters the interpreter, and neither can the crash prevention. Recording such calls in the AOT profile doesn't " +
				$"help: the AOT compiler can fail on them, which fails the build. Pass the struct with 'in', or {remedy}. To remove the warning, add " +
				$"UNOWASM010 to NoWarn. See {DocumentationUrl}");
		}

		foreach (string limit in result.Limits)
		{
			LogWarning("UNOWASM011",
				$"[Uno] To keep large or recursive generic code from taking too long, the crash prevention of this profile-guided AOT build stopped " +
				$"following {limit}. The app can still crash in code that uses them, as described in {DocumentationUrl}. To prevent the crash, run " +
				$"that code while you record the AOT profile, so that it's compiled. Please report this warning at {IssuesUrl}. To remove the " +
				"warning, add UNOWASM011 to NoWarn.");
		}

		return true;
	}

	// "its parameter 2 ('m'), of type System.Numerics.Matrix4x4, has a default value"
	private static string DescribeCause(UnenterableMethods.Finding finding) => finding.TypeArgument is string typeArgument
		? $"its {finding.Parameter}, whose type argument is the struct {typeArgument} here, has {finding.Flags}"
		: $"its {finding.Parameter}, of type {finding.Type}, has {finding.Flags}";

	// What makes the type different: the runtime maps the other structs to new types, which don't have the flags
	// (WrapperSignature.KeepsParameterAttributes).
	private static string DescribeType(UnenterableMethods.Finding finding)
	{
		string type = finding.IsFunctionPointer
			? "The runtime passes a function pointer"
			: "Unlike other structs, the runtime passes a struct with an explicit layout or many fields";

		return $"{type} as it is between compiled and interpreted code, along with what the default value or attribute adds to the parameter, and " +
			"then can't find the code that does it.";
	}

	private void LogWarning(string code, string message)
		=> Log.LogWarning(
			subcategory: null,
			warningCode: code,
			helpKeyword: null,
			file: null, lineNumber: 0, columnNumber: 0, endLineNumber: 0, endColumnNumber: 0,
			message: message);

	private static void DeleteFiles(AotWrapperSeedGenerator.SeedFiles files)
	{
		foreach (string file in new[] { files.Assembly, files.Profile, files.ExpectedWrappers })
		{
			try
			{
				File.Delete(file);
			}
			catch (Exception e) when (e is IOException or UnauthorizedAccessException)
			{
			}
		}
	}
}

/// <summary>
/// <see cref="GenerateAOTWrapperSeedsTask"/> under the name that the targets register, which the package build versions with the task assembly's
/// (UpdateTasksSHA in Uno.Wasm.Bootstrap.csproj), so that tests use the base class.
/// </summary>
public sealed class GenerateAOTWrapperSeedsTask_v0 : GenerateAOTWrapperSeedsTask
{
}
