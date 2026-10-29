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
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.Build.Framework;
using Uno.Wasm.Bootstrap.AOTWrapperSeeding;

namespace Uno.Wasm.Bootstrap;

/// <summary>
/// Checks with <c>llvm-nm</c> that the AOT compilation emitted every interpreter entry wrapper <see cref="GenerateAOTWrapperSeedsTask"/> expects.
/// The targets run it as <see cref="ValidateAOTWrapperSeedsTask_v0"/>.
/// </summary>
public class ValidateAOTWrapperSeedsTask : Microsoft.Build.Utilities.Task, ICancelableTask
{
	// llvm-nm lists the functions of a large app's bitcode in under a second.
	private static readonly TimeSpan LlvmNmTimeout = TimeSpan.FromMinutes(5);

	// The most UNOWASM006 errors one build logs; one more error counts the others.
	private const int MaxReportedMissingWrappers = 50;

	private readonly CancellationTokenSource _cancellation = new();

	/// <summary>The expected wrappers file written by <see cref="GenerateAOTWrapperSeedsTask"/>.</summary>
	[Required]
	public string ExpectedWrappers { get; set; } = "";

	/// <summary>
	/// The bitcode file to look for the wrappers in: <c>aot-instances.dll.bc</c>, as the seeding requires deduplication. Not required, so that a
	/// build whose AOT compilation didn't write it gets UNOWASM007 rather than an MSBuild error.
	/// </summary>
	public string BitcodeFile { get; set; } = "";

	/// <summary>The path of <c>llvm-nm</c>.</summary>
	[Required]
	public string LlvmNm { get; set; } = "";

	/// <summary>The page the build messages point to.</summary>
	[Required]
	public string DocumentationUrl { get; set; } = "";

	/// <summary>The page the build messages ask to report problems on.</summary>
	[Required]
	public string IssuesUrl { get; set; } = "";

	public void Cancel()
	{
		try
		{
			_cancellation.Cancel();
		}
		catch (ObjectDisposedException)
		{
			// The check has finished.
		}
	}

	public override bool Execute()
	{
		try
		{
			return Check();
		}
		finally
		{
			_cancellation.Dispose();
		}
	}

	private bool Check()
	{
		var stopwatch = Stopwatch.StartNew();

		if (string.IsNullOrEmpty(BitcodeFile))
		{
			LogCheckFailure("the AOT compilation didn't write aot-instances.dll.bc");
			return false;
		}

		string[] expectedWrappers;

		try
		{
			expectedWrappers = File.ReadAllLines(ExpectedWrappers);
		}
		catch (Exception e) when (e is IOException or UnauthorizedAccessException)
		{
			LogCheckFailure($"couldn't read {ExpectedWrappers}: {e.Message}");
			return false;
		}

		string path = Path.GetFullPath(BitcodeFile);
		var functions = new List<string>();
		var run = ToolRunner.Run(
			LlvmNm,
			$"--defined-only \"{path}\"",
			line =>
			{
				if (AotWrapperCheck.ParseFunctionSymbol(line) is string function)
				{
					functions.Add(function);
				}
			},
			LlvmNmTimeout,
			_cancellation.Token);

		if (_cancellation.IsCancellationRequested)
		{
			return false;
		}

		if (run.StartFailure != null)
		{
			LogCheckFailure($"couldn't start {LlvmNm}: {run.StartFailure}");
			return false;
		}

		if (run.TimedOut)
		{
			LogCheckFailure($"llvm-nm didn't finish reading {path} in {LlvmNmTimeout.TotalMinutes} minutes");
			return false;
		}

		if (run.ExitCode != 0)
		{
			LogCheckFailure($"llvm-nm exited with code {run.ExitCode} reading {path}{(run.Error.Length > 0 ? ": " + run.Error : "")}");
			return false;
		}

		var result = AotWrapperCheck.Evaluate(expectedWrappers, functions);

		Log.LogMessage(MessageImportance.Normal,
			$"[Uno] AOT wrapper seeding check: {result.GsharedvtInFound} of {result.GsharedvtInExpected} gsharedvt_in_sig and {result.InterpInFound} of " +
			$"{result.InterpInExpected} interp_in wrappers found in {Path.GetFileName(path)} in {stopwatch.ElapsedMilliseconds} ms.");

		foreach (var missing in result.MissingWrappers.Take(MaxReportedMissingWrappers))
		{
			string counts = missing.Expected > 1 ? $", {missing.Found} of {missing.Expected} found" : "";

			LogMissingCode(
				$"[Uno] The profile-guided AOT build lacks code that {missing.Origin} needs, so the app may crash when it reaches that method. Please " +
				$"report this error at {IssuesUrl}. See {DocumentationUrl} (missing: {missing.Name}{counts})");
		}

		if (result.MissingWrappers.Count > MaxReportedMissingWrappers)
		{
			LogMissingCode(
				$"[Uno] The profile-guided AOT build lacks the code of {result.MissingWrappers.Count - MaxReportedMissingWrappers} more methods, " +
				$"which aren't listed. Please report this error at {IssuesUrl}. See {DocumentationUrl}");
		}

		return !Log.HasLoggedErrors;
	}

	private void LogMissingCode(string message)
		=> Log.LogError(
			subcategory: null,
			errorCode: "UNOWASM006",
			helpKeyword: null,
			file: null, lineNumber: 0, columnNumber: 0, endLineNumber: 0, endColumnNumber: 0,
			message: message);

	private void LogCheckFailure(string reason)
		=> Log.LogError(
			subcategory: null,
			errorCode: "UNOWASM007",
			helpKeyword: null,
			file: null, lineNumber: 0, columnNumber: 0, endLineNumber: 0, endColumnNumber: 0,
			message: $"[Uno] Couldn't check the crash prevention of this profile-guided AOT build: {reason}. Please report this error at {IssuesUrl}. " +
				$"To skip the check, set WasmShellEnableAOTWrapperSeedingCheck to false. See {DocumentationUrl}");
}

/// <summary>
/// <see cref="ValidateAOTWrapperSeedsTask"/> under the name that the targets register, which the package build versions with the task assembly's
/// (UpdateTasksSHA in Uno.Wasm.Bootstrap.csproj), so that tests use the base class.
/// </summary>
public sealed class ValidateAOTWrapperSeedsTask_v0 : ValidateAOTWrapperSeedsTask
{
}
