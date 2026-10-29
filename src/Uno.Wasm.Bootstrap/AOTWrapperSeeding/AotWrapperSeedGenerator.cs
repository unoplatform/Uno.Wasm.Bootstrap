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
using System.Text;
using System.Threading;
using Mono.Cecil;
using Mono.Profiler.Aot;

namespace Uno.Wasm.Bootstrap.AOTWrapperSeeding;

/// <summary>
/// Writes the seed assembly, the AOT profile that lists its seed methods, and the list of interpreter entry wrappers the AOT compilation is
/// expected to emit. See <see cref="SignatureCollector"/> and <see cref="SeedAssemblyWriter"/>.
/// </summary>
internal static class AotWrapperSeedGenerator
{
	private const string CoreLibraryName = "System.Private.CoreLib";

	/// <param name="Unchecked">The seeded methods whose wrapper names couldn't be predicted, with why: the check doesn't look for their wrappers.</param>
	/// <param name="Skipped">The assemblies, types, methods, references and seeds that couldn't be read or written, with why.</param>
	/// <param name="Limits">The generic instances that weren't followed, past the collector's limits (UNOWASM011).</param>
	/// <param name="UnenterableMethods">The methods whose interpreter entry wrapper the runtime can't find (UNOWASM010).</param>
	/// <param name="UndeterminedMethods">The methods that couldn't be checked for <paramref name="UnenterableMethods"/>, with why.</param>
	/// <param name="UnprotectedStructs">
	/// The structs that the runtime can't map (at the root of cyclic layouts, or with 16 type arguments or more), whose seeds were left out, and
	/// whose methods UNOWASM010 can't check (UNOWASM010).
	/// </param>
	/// <param name="Collected">What the collector went through: interface methods, virtual methods, delegate targets and constructed generic types.</param>
	public sealed record Result(
		int AssemblyCount,
		SignatureCollector.Statistics Collected,
		int SeedCount,
		int SeedMethodCount,
		int GsharedvtInWrapperCount,
		int InterpInWrapperCount,
		IReadOnlyList<string> Unchecked,
		IReadOnlyList<string> Skipped,
		IReadOnlyList<string> Limits,
		IReadOnlyList<UnenterableMethods.Finding> UnenterableMethods,
		IReadOnlyList<UnenterableMethods.Undetermined> UndeterminedMethods,
		IReadOnlyList<UnprotectedStruct> UnprotectedStructs,
		TimeSpan Elapsed);

	/// <param name="Type">
	/// The struct that the runtime can't map (<see cref="WrapperSignature.FindUnsharableStruct"/>), the definition of a generic one.
	/// </param>
	/// <param name="Reason">Why the runtime can't map it.</param>
	/// <param name="Method">
	/// A method that takes or returns it, or a struct that holds it, by value, as <see cref="SeedOrigin"/> names it: the first whose seed was left
	/// out for it, or else the first that UNOWASM010 couldn't check for it.
	/// </param>
	public sealed record UnprotectedStruct(string Type, WrapperSignature.UnsharableReason Reason, string Method);

	/// <summary>
	/// The files to write: the seed assembly, whose file name is its assembly name, its AOT profile, and the expected wrappers.
	/// </summary>
	public sealed record SeedFiles(string Assembly, string Profile, string ExpectedWrappers)
	{
		public string AssemblyName => Path.GetFileNameWithoutExtension(Assembly);
	}

	/// <summary>
	/// Collects the signatures to seed from the assemblies about to be AOT-compiled and writes the files. A file is only written when its content
	/// changes: the AOT compiler compiles everything again when one of its inputs is newer than its output.
	/// </summary>
	/// <param name="seedsPerMethod">The seeds each seed method calls; tests put them all in one.</param>
	/// <param name="cancellationToken">Stops the collection, before any file is written.</param>
	public static Result Generate(
		IEnumerable<string> assemblyPaths,
		SeedFiles files,
		int seedsPerMethod = SeedAssemblyWriter.SeedsPerMethod,
		CancellationToken cancellationToken = default)
	{
		var stopwatch = Stopwatch.StartNew();
		string assemblyName = files.AssemblyName;

		// The AOT compiler takes one assembly of each name, as it copies them all into one folder. The targets add the seed assembly to the AOT
		// inputs after the generation, so leaving it out only guards against inputs that would already have it. The first method collected for a
		// signature gives its seed its types, so the inputs are read in the order of their names rather than in the build's order.
		var paths = assemblyPaths
			.Where(p => p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
			.Where(p => !Path.GetFileNameWithoutExtension(p).Equals(assemblyName, StringComparison.OrdinalIgnoreCase))
			.GroupBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase)
			.Select(g => g.First())
			.OrderBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase)
			.ToList();

		using var generation = CecilTypes.BeginGeneration();
		using var resolver = new PathAssemblyResolver(paths);
		var coreLibrary = resolver.Resolve(new AssemblyNameReference(CoreLibraryName, null)) ?? throw new InvalidOperationException(
			resolver.GetFailure(CoreLibraryName) is string failure
				? $"{CoreLibraryName} couldn't be read: {failure}"
				: $"{CoreLibraryName} is not among the AOT inputs.");

		var collector = new SignatureCollector { CancellationToken = cancellationToken };
		var unenterableMethods = new List<UnenterableMethods.Finding>();
		var undeterminedMethods = new List<UnenterableMethods.Undetermined>();
		var unsharableMethods = new List<UnenterableMethods.UnsharableMethod>();
		var skipped = new List<string>();
		int assemblyCount = 0;

		// An assembly or module that can't be read only skips itself (the AOT compiler can't read it either).
		foreach (string path in paths)
		{
			if (resolver.Read(path) is not { } assembly)
			{
				skipped.Add($"{path}: {resolver.GetFailure(Path.GetFileNameWithoutExtension(path))}");
				continue;
			}

			assemblyCount++;

			foreach (var module in assembly.Modules)
			{
				try
				{
					collector.AddModule(module);
					UnenterableMethods.Find(module, unenterableMethods, undeterminedMethods, unsharableMethods);
				}
				catch (Exception e) when (e is not (OutOfMemoryException or OperationCanceledException))
				{
					skipped.Add($"{path}: {e.GetType().Name}: {e.Message}");
				}
			}
		}

		UnenterableMethods.Find(collector.FlaggedMethodInstances, unenterableMethods, undeterminedMethods, unsharableMethods);

		// A method with flagged parameters of both a struct type and a generic parameter's type is found twice: one warning names the first.
		var reportedMethods = new HashSet<string>(StringComparer.Ordinal);
		unenterableMethods.RemoveAll(f => !reportedMethods.Add(f.Method));

		// The AOT compiler overflows its stack on a seed with a value type whose layout is cyclic, and asserts on one with a generic struct of 16 type
		// arguments or more, which would fail the build (WrapperSignature.FindUnsharableStruct): such seeds are left out. Each struct is reported
		// once, with a method that takes it, or a struct that holds it, by value.
		var seeds = new List<SeedSignature>();
		var unprotectedStructs = new Dictionary<string, UnprotectedStruct>(StringComparer.Ordinal);
		var unsharableStructs = new Dictionary<string, WrapperSignature.UnsharableStruct?>(StringComparer.Ordinal);

		foreach (var seed in collector.Seeds.OrderBy(s => s.Key, StringComparer.Ordinal))
		{
			if (seed.ParameterTypes.Prepend(seed.ReturnType).Select(t => FindUnsharableStruct(t, unsharableStructs)).FirstOrDefault(s => s != null)
				is { } unsharable)
			{
				AddUnprotectedStruct(unprotectedStructs, unsharable, seed.Origin.ToString());
			}
			else
			{
				seeds.Add(seed);
			}
		}

		foreach (var method in unsharableMethods)
		{
			AddUnprotectedStruct(unprotectedStructs, method.Struct, method.Method);
		}

		var written = SeedAssemblyWriter.Write(coreLibrary, resolver, seeds, assemblyName, seedsPerMethod);
		WriteIfChanged(files.Assembly, written.Image);
		WriteIfChanged(files.Profile, WriteProfile(assemblyName, written.MethodNames));

		var expected = PredictWrappers(written.Seeds);
		WriteIfChanged(files.ExpectedWrappers, Encoding.UTF8.GetBytes(string.Concat(expected.Lines.Select(l => l + "\n"))));

		skipped.AddRange(collector.Skipped.Concat(CecilTypes.UnexpectedResolutionFailures).Concat(written.Skipped));

		return new Result(assemblyCount, collector.Collected, written.Seeds.Count, written.MethodNames.Count, expected.GsharedvtInCount,
			expected.InterpInCount, expected.Unchecked, skipped, collector.Limits, unenterableMethods, undeterminedMethods,
			unprotectedStructs.Values.ToList(), stopwatch.Elapsed);
	}

	// The struct that the runtime can't map in a type of a seed (WrapperSignature.FindUnsharableStruct), once per type, as seeds share most of their
	// types and a walk through a large value type takes long. Only the value types that the runtime maps by their layout can have one.
	private static WrapperSignature.UnsharableStruct? FindUnsharableStruct(TypeReference type, Dictionary<string, WrapperSignature.UnsharableStruct?> found)
	{
		if (CecilTypes.StripModifiers(type) is FunctionPointerType || PrimitiveTypes.GetShared(type) != null)
		{
			return null;
		}

		string identity = CecilTypes.GetIdentity(type);

		if (!found.TryGetValue(identity, out var unsharable))
		{
			unsharable = WrapperSignature.FindUnsharableStruct(type);
			found.Add(identity, unsharable);
		}

		return unsharable;
	}

	// A generic struct stands for all its instances, by its definition: the type arguments of a cycle's root are those of the runtime's mapping, and
	// every instance of a generic struct has as many type arguments.
	private static void AddUnprotectedStruct(Dictionary<string, UnprotectedStruct> structs, WrapperSignature.UnsharableStruct unsharable, string method)
	{
		var type = unsharable.Root is GenericInstanceType instance ? instance.ElementType : unsharable.Root;
		string identity = CecilTypes.GetIdentity(type);

		if (!structs.ContainsKey(identity))
		{
			structs.Add(identity, new UnprotectedStruct(MemberNames.Type(type, true), unsharable.Reason, method));
		}
	}

	/// <param name="Lines">
	/// One line per distinct wrapper: its name, a tab, and a method that needs it. interp_in names can repeat, as distinct interp_in wrappers can
	/// share one.
	/// </param>
	/// <param name="Unchecked">The seeds whose names couldn't be predicted, with why.</param>
	private sealed record ExpectedWrappers(List<string> Lines, int GsharedvtInCount, int InterpInCount, List<string> Unchecked);

	// The names only verify the build: a seed whose names can't be predicted is still seeded.
	private static ExpectedWrappers PredictWrappers(IEnumerable<SeedSignature> seeds)
	{
		var wrappers = new List<(string Name, SeedOrigin Origin)>();
		var gsharedvtInNames = new HashSet<string>(StringComparer.Ordinal);
		var interpInKeys = new HashSet<string>(StringComparer.Ordinal);
		var uncheckedSeeds = new List<string>();

		foreach (var seed in seeds)
		{
			try
			{
				if (WrapperSignature.GetGsharedvtInWrapperName(seed.HasThis, seed.ReturnType, seed.ParameterTypes) is string name)
				{
					if (gsharedvtInNames.Add(name))
					{
						wrappers.Add((name, seed.Origin));
					}
				}
				else if (WrapperSignature.GetInterpInWrapper(seed.HasThis, seed.ReturnType, seed.ParameterTypes) is var (interpIn, key)
					&& interpInKeys.Add(key))
				{
					wrappers.Add((interpIn, seed.Origin));
				}
			}
			catch (Exception e) when (e is not OutOfMemoryException)
			{
				uncheckedSeeds.Add($"{seed.Origin}: {(e is NotSupportedException ? "" : e.GetType().Name + ": ")}{e.Message}");
			}
		}

		var lines = wrappers.OrderBy(w => w.Name, StringComparer.Ordinal).Select(w => $"{w.Name}\t{w.Origin}").ToList();
		return new ExpectedWrappers(lines, gsharedvtInNames.Count, interpInKeys.Count, uncheckedSeeds);
	}

	// An AOT profile (the format load_profile_file in aot-compiler.c reads) that lists the seed methods.
	private static byte[] WriteProfile(string assemblyName, IReadOnlyList<string> methodNames)
	{
		var module = new ModuleRecord(0, assemblyName, "");
		var type = new TypeRecord(1, module, assemblyName + "." + SeedAssemblyWriter.SeedsTypeName, null);
		var methods = methodNames.Select((name, i) => new MethodRecord(2 + i, type, null, name, "void()", 0)).ToArray();

		using var stream = new MemoryStream();
		new ProfileWriter().WriteAllData(stream, new ProfileData([module], [type], methods));
		return stream.ToArray();
	}

	private static void WriteIfChanged(string path, byte[] content)
	{
		if (File.Exists(path) && new FileInfo(path).Length == content.Length && File.ReadAllBytes(path).SequenceEqual(content))
		{
			return;
		}

		Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
		File.WriteAllBytes(path, content);
	}

	/// <summary>
	/// Resolves assemblies among the AOT inputs only, which is what the AOT compiler sees.
	/// </summary>
	private sealed class PathAssemblyResolver : IAssemblyResolver
	{
		private readonly Dictionary<string, string> _paths;
		private readonly Dictionary<string, AssemblyDefinition?> _assemblies = new(StringComparer.OrdinalIgnoreCase);
		private readonly Dictionary<string, string> _failures = new(StringComparer.OrdinalIgnoreCase);

		// Generate passes one path per file name.
		public PathAssemblyResolver(IEnumerable<string> paths)
		{
			_paths = paths.ToDictionary(p => Path.GetFileNameWithoutExtension(p), StringComparer.OrdinalIgnoreCase);
		}

		public AssemblyDefinition? Read(string path) => Load(Path.GetFileNameWithoutExtension(path));

		/// <summary>
		/// Returns why the assembly of this name couldn't be read, if it couldn't.
		/// </summary>
		public string? GetFailure(string name) => _failures.TryGetValue(name, out string? failure) ? failure : null;

		public AssemblyDefinition? Resolve(AssemblyNameReference name) => Load(name.Name);

		public AssemblyDefinition? Resolve(AssemblyNameReference name, ReaderParameters parameters) => Load(name.Name);

		private AssemblyDefinition? Load(string name)
		{
			if (_assemblies.TryGetValue(name, out var assembly))
			{
				return assembly;
			}

			assembly = null;

			if (_paths.TryGetValue(name, out string? path))
			{
				try
				{
					assembly = AssemblyDefinition.ReadAssembly(path, new ReaderParameters
					{
						AssemblyResolver = this,
						ReadingMode = ReadingMode.Deferred,
						InMemory = true,
					});
				}
				catch (Exception e) when (e is not OutOfMemoryException)
				{
					// Not a .NET assembly, a file that can't be read, or metadata the reader can't handle: references to it don't resolve, and
					// Generate reports it.
					_failures[name] = e is BadImageFormatException ? "not a .NET assembly" : $"{e.GetType().Name}: {e.Message}";
				}
			}

			_assemblies.Add(name, assembly);
			return assembly;
		}

		public void Dispose()
		{
			foreach (var assembly in _assemblies.Values)
			{
				assembly?.Dispose();
			}

			_assemblies.Clear();
		}
	}
}
