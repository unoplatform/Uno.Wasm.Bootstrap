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
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Uno.Wasm.Bootstrap.AOTWrapperSeeding;
using Wide15 = Uno.Wasm.Bootstrap.UnitTests.AotSeedingWide15<int, int, int, int, int, int, int, int, int, int, int, int, int, int, int>;
using Wide16 = Uno.Wasm.Bootstrap.UnitTests.AotSeedingWide16<int, int, int, int, int, int, int, int, int, int, int, int, int, int, int, int>;

namespace Uno.Wasm.Bootstrap.UnitTests;

[TestClass]
public class Given_AOTWrapperSeeding
{
	private const string SeedAssemblyName = TestSeedFiles.AssemblyName;
	private const string TwinNamespace = "AotSeedingTwins";

	// All the twin seeds go in one seed method.
	private const int TwinSeedsPerMethod = 100_000;

	private static string _outputDirectory = "";
	private static AotWrapperSeedGenerator.Result _result = null!;
	private static AotWrapperSeedGenerator.SeedFiles _files = null!;
	private static AotWrapperSeedGenerator.SeedFiles _twinFiles = null!;
	private static string[] _twinAssemblies = [];
	private static ModuleDefinition _testModule = null!;
	private static HashSet<string> _expectedNames = [];
	private static AssemblyDefinition _seedAssembly = null!;
	private static TypeDefinition _seeds = null!;

	[ClassInitialize]
	public static void Generate(TestContext context)
	{
		string runtimeDirectory = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
		string[] assemblies =
		[
			typeof(Given_AOTWrapperSeeding).Assembly.Location,
			typeof(object).Assembly.Location,
			Path.Combine(runtimeDirectory, "System.Runtime.dll"),
		];

		_outputDirectory = Path.Combine(Path.GetTempPath(), "uno-aot-wrapper-seeding-" + Guid.NewGuid().ToString("N"));
		_files = TestSeedFiles.In(_outputDirectory);
		_result = AotWrapperSeedGenerator.Generate(assemblies, _files);
		_expectedNames = new(File.ReadAllLines(_files.ExpectedWrappers).Select(l => l.Split('\t')[0]), StringComparer.Ordinal);
		_seedAssembly = AssemblyDefinition.ReadAssembly(_files.Assembly, new ReaderParameters { InMemory = true });
		_seeds = GetSeedsType(_seedAssembly);
		_twinFiles = GenerateTwins(Path.Combine(_outputDirectory, "twins"));

		var resolver = new DefaultAssemblyResolver();
		resolver.AddSearchDirectory(Path.GetDirectoryName(typeof(Given_AOTWrapperSeeding).Assembly.Location)!);
		resolver.AddSearchDirectory(runtimeDirectory);
		_testModule = ModuleDefinition.ReadModule(typeof(Given_AOTWrapperSeeding).Assembly.Location, new ReaderParameters { AssemblyResolver = resolver });
	}

	// Two assemblies that define types of the same names, AotSeedingTwins.Value (with explicit layout, so wrapper names keep it) and
	// AotSeedingTwins.Service, and the Mark methods of IConsumer and IStore, whose seed is the same.
	private static AotWrapperSeedGenerator.SeedFiles GenerateTwins(string directory)
	{
		Directory.CreateDirectory(directory);

		string twinA = WriteTwinAssembly(directory, "AotSeedingTwinA", (module, value) =>
		{
			var consumer = AddTwinType(module, "IConsumer", true);
			AddTwinMethod(consumer, "Take", [value, .. Enumerable.Repeat(module.ImportReference(typeof(int)), 8)]);
			AddTwinMethod(consumer, "Put", [value]);
			AddTwinMethod(consumer, "Mark", [module.ImportReference(typeof(bool)), .. Enumerable.Repeat(module.ImportReference(typeof(long)), 2)]);
			AddTwinType(module, "Service", false);
		});

		// The same methods on another Value, Put with an int more, and a Service that implements an interface method of a signature no other
		// assembly has.
		string twinB = WriteTwinAssembly(directory, "AotSeedingTwinB", (module, value) =>
		{
			var store = AddTwinType(module, "IStore", true);
			AddTwinMethod(store, "Take", [value, .. Enumerable.Repeat(module.ImportReference(typeof(int)), 8)]);
			AddTwinMethod(store, "Put", [value, module.ImportReference(typeof(int))]);
			AddTwinMethod(store, "Mark", [module.ImportReference(typeof(byte)), .. Enumerable.Repeat(module.ImportReference(typeof(long)), 2)]);

			var mixer = AddTwinType(module, "IMixer", true);
			var service = AddTwinType(module, "Service", false);
			service.Interfaces.Add(new InterfaceImplementation(mixer));

			var doubleType = module.ImportReference(typeof(double));
			var longType = module.ImportReference(typeof(long));
			TypeReference[] mixParameters = [doubleType, longType, doubleType, longType, doubleType, longType, doubleType, longType, doubleType];
			AddTwinMethod(mixer, "Mix", mixParameters);
			AddTwinMethod(service, "Mix", mixParameters);
		});

		_twinAssemblies = [twinA, twinB, typeof(object).Assembly.Location];
		var files = TestSeedFiles.In(Path.Combine(directory, "seeds"));
		AotWrapperSeedGenerator.Generate(_twinAssemblies, files, TwinSeedsPerMethod);
		return files;
	}

	private static string WriteTwinAssembly(string directory, string name, Action<ModuleDefinition, TypeDefinition> addTypes)
	{
		using var assembly = AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition(name, new Version(1, 0, 0, 0)), name, ModuleKind.Dll);
		var module = assembly.MainModule;

		var attributes = TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.ExplicitLayout;
		var value = new TypeDefinition(TwinNamespace, "Value", attributes, module.ImportReference(typeof(ValueType)));
		value.Fields.Add(new FieldDefinition("X", FieldAttributes.Public, module.ImportReference(typeof(int))) { Offset = 0 });
		module.Types.Add(value);

		addTypes(module, value);

		string path = Path.Combine(directory, name + ".dll");
		assembly.Write(path);
		return path;
	}

	private static TypeDefinition AddTwinType(ModuleDefinition module, string name, bool isInterface)
	{
		var type = isInterface
			? new TypeDefinition(TwinNamespace, name, TypeAttributes.Public | TypeAttributes.Interface | TypeAttributes.Abstract)
			: new TypeDefinition(TwinNamespace, name, TypeAttributes.Public, module.ImportReference(typeof(object)));

		module.Types.Add(type);
		return type;
	}

	// A virtual method that only returns, or an abstract one in an interface.
	private static void AddTwinMethod(TypeDefinition type, string name, TypeReference[] parameterTypes)
	{
		var attributes = MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig | MethodAttributes.NewSlot;
		var method = new MethodDefinition(
			name, attributes | (type.IsInterface ? MethodAttributes.Abstract : 0), type.Module.ImportReference(typeof(void)));

		for (int i = 0; i < parameterTypes.Length; i++)
		{
			method.Parameters.Add(new ParameterDefinition("p" + i, ParameterAttributes.None, parameterTypes[i]));
		}

		if (!type.IsInterface)
		{
			method.Body.GetILProcessor().Emit(OpCodes.Ret);
		}

		type.Methods.Add(method);
	}

	[ClassCleanup]
	public static void Cleanup()
	{
		_testModule?.Dispose();
		_seedAssembly?.Dispose();

		try
		{
			Directory.Delete(_outputDirectory, true);
		}
		catch (Exception e) when (e is IOException or UnauthorizedAccessException)
		{
		}
	}

	private static TypeDefinition GetSeedsType(AssemblyDefinition assembly)
		=> assembly.MainModule.GetType(SeedAssemblyName + "." + SeedAssemblyWriter.SeedsTypeName);

	private static void AssertExpected(string wrapper, string? what = null)
		=> Assert.IsTrue(_expectedNames.Contains(wrapper), $"{wrapper}{(what == null ? "" : $" ({what})")} is not among the expected wrappers.");

	// The names below are the ones the .NET 10 AOT compiler (10.0.12) gave these wrappers in the bitcode of an AOT build.
	[TestMethod]
	[DataRow("gsharedvt_in_sig_i8_this_i8i8", DisplayName = "Instance method, long (long, long)")]
	[DataRow("gsharedvt_in_sig_i8_i8i8", DisplayName = "Static method, long (long, long)")]
	[DataRow("gsharedvt_in_sig_cl1e_Mono_dValueTuple_601_3clong_3e__this_", DisplayName = "Instance method returning a DateTime")]
	[DataRow(
		"wrapper_other_object_interp_in_generic_intptr_intptr_intptr_intptr_long_long_double_double_intptr",
		DisplayName = "Instance method with 9 parameters")]
	[DataRow(
		"wrapper_other_object_interp_in_generic_Uno_Wasm_Bootstrap_UnitTests_AotSeeding__tat_intptr_intptr_intptr_intptr_intptr_intptr_intptr_intptr",
		DisplayName = "Instance method with 9 parameters, one of a type with a non-ASCII name")]
	[DataRow("gsharedvt_in_sig_void_this_cl26_Mono_dValueTuple_602_3cint_2c_20int_3e_i1i1i1", DisplayName = "Instance method taking an inline array")]

	// get_wrapper_shared_type_full re-inflates AotSeedingBox<T> with Mono.ValueTuple`2<byte, int>, the shared type of AotSeedingPacked, before
	// it maps the fields: that field then maps to Mono.ValueTuple`2<int, int> (8 bytes aligned to 4).
	[DataRow(
		"gsharedvt_in_sig_void_this_cl40_Mono_dValueTuple_601_3cMono_dValueTuple_602_3cint_2c_20int_3e_3e_",
		DisplayName = "Instance method taking a generic value type over a packed one")]
	public void When_Generating_Then_The_Wrappers_Are_Named_Like_The_Runtime(string wrapper)
	{
		AssertExpected(wrapper);
	}

	// The AOT compiler escapes the UTF-8 bytes of type names as C chars, which are signed on x64 build machines: this is the name the AOT
	// compiler emitted on Windows x64. Elsewhere, only a build checks the name (see WrapperSignature.IsCharSigned).
	[TestMethod]
	public void When_Generating_Then_Non_Ascii_Type_Names_Are_Escaped_Like_The_Runtime()
	{
		if (RuntimeInformation.ProcessArchitecture != Architecture.X64)
		{
			Assert.Inconclusive("The escaped name is only known on x64 build machines.");
		}

		const string Wrapper = "gsharedvt_in_sig_void_this_cl40_Uno_dWasm_dBootstrap_dUnitTests_dAotSeeding_ffffffc3_ffffff89tat_";
		AssertExpected(Wrapper);
	}

	// The lambda of AotSeedingSamples.CreateDelegate is the only method of this signature.
	[TestMethod]
	public void When_Generating_Then_Delegate_Targets_Are_Seeded()
	{
		var parameterTypes = new[] { typeof(decimal), typeof(Guid), typeof(TimeSpan), typeof(ushort), typeof(sbyte) }
			.Select(_testModule.ImportReference)
			.ToList();

		string wrapper = WrapperSignature.GetGsharedvtInWrapperName(true, _testModule.ImportReference(typeof(short)), parameterTypes)!;

		AssertExpected(wrapper, "AotSeedingSamples.CreateDelegate's lambda");
	}

	// get_wrapper_shared_type_full keeps function pointers as they are, and mono_type_get_desc describes them as *(), which gsharedvt_in_sig
	// names escape and interp_in names drop.
	[TestMethod]
	[DataRow("gsharedvt_in_sig_void_this_cl9__2a_28_29_", DisplayName = "Instance method taking a function pointer")]
	[DataRow(
		"wrapper_other_object_interp_in_generic___Mono_ValueTuple_2_long_long_Mono_ValueTuple_2_long_long_Mono_ValueTuple_2_long_long_" +
		"Mono_ValueTuple_2_long_long_Mono_ValueTuple_2_long_long_Mono_ValueTuple_2_long_long_Mono_ValueTuple_2_long_long_Mono_ValueTuple_2_long_long",
		DisplayName = "Instance method with 9 parameters, a function pointer first")]
	public void When_A_Signature_Has_A_Function_Pointer_Then_It_Is_Seeded(string wrapper)
	{
		AssertExpected(wrapper);
	}

	// Function pointers of different signatures are different types: each signature gets its own seed.
	[TestMethod]
	public void When_Signatures_Only_Differ_In_Function_Pointer_Types_Then_Each_Is_Seeded()
	{
		var functionPointers = _seeds.Methods
			.Where(m => m.Parameters.Count == 1 && m.Parameters[0].ParameterType is FunctionPointerType)
			.Select(m => ((FunctionPointerType)m.Parameters[0].ParameterType).Parameters.Single().ParameterType.FullName)
			.ToList();

		CollectionAssert.IsSubsetOf(new[] { "System.Int32", "System.Int64" }, functionPointers);
	}

	// TypedReference, ArgIterator and RuntimeArgumentHandle can't be passed by the seeds, and generic methods have no concrete signature.
	[TestMethod]
	public void When_A_Signature_Has_Types_A_Seed_Cannot_Pass_Then_It_Is_Not_Seeded()
	{
		var parameterTypes = _seeds.Methods
			.SelectMany(m => m.Parameters)
			.Select(p => p.ParameterType)
			.ToList();

		CollectionAssert.DoesNotContain(
			parameterTypes.Select(t => t.FullName).Distinct().ToList(), "System.TypedReference", "A seed passes a TypedReference.");
		Assert.IsFalse(parameterTypes.Any(t => t.FullName is "System.ArgIterator" or "System.RuntimeArgumentHandle"));
		Assert.IsFalse(parameterTypes.Any(t => t.ContainsGenericParameter));
		Assert.IsTrue(_result.Collected.Unsupported > 0);
	}

	// Seed methods that the runtime's JIT compiles have valid IL: stack, locals and calls.
	[TestMethod]
	public void When_Loading_The_Seed_Assemblies_Then_Their_Methods_Compile()
	{
		foreach (var (assembly, referenceDirectory) in SeedAssemblies)
		{
			UseSeedMethods(assembly, referenceDirectory, methods =>
			{
				foreach (var method in methods)
				{
					RuntimeHelpers.PrepareMethod(method.MethodHandle);
				}
			});
		}
	}

	// The targets throw: the seed methods must not call them, so that the AOT compiler drops the calls once it has their signatures.
	[TestMethod]
	public void When_Running_The_Seed_Methods_Then_They_Call_Nothing()
	{
		foreach (var (assembly, referenceDirectory) in SeedAssemblies)
		{
			UseSeedMethods(assembly, referenceDirectory, methods =>
			{
				foreach (var method in methods.Where(m => m.Name.StartsWith("S", StringComparison.Ordinal)))
				{
					method.Invoke(null, null);
				}
			});
		}
	}

	// The AOT compiler records the signatures of the calls behind a conditional branch, and would skip those after an unconditional one.
	[TestMethod]
	public void When_Generating_Then_The_Seed_Methods_Branch_Over_Their_Calls_On_A_Condition()
	{
		foreach (var method in _seeds.Methods.Where(m => m.Name.StartsWith("S", StringComparison.Ordinal)))
		{
			var instructions = method.Body.Instructions;

			Assert.AreEqual(OpCodes.Ldc_I4_0, instructions[0].OpCode, method.Name);
			Assert.AreEqual(OpCodes.Brfalse, instructions[1].OpCode, method.Name);
			Assert.AreSame(instructions[^1], instructions[1].Operand, method.Name);
			Assert.AreEqual(OpCodes.Ret, instructions[^1].OpCode, method.Name);
		}
	}

	private static (string Assembly, string ReferenceDirectory)[] SeedAssemblies =>
	[
		(_files.Assembly, Path.GetDirectoryName(_files.Assembly)!),
		(_twinFiles.Assembly, Path.Combine(_outputDirectory, "twins")),
	];

	// Loads the seed assembly in a context of its own, and passes its methods on.
	private static void UseSeedMethods(string seedAssembly, string referenceDirectory, Action<System.Reflection.MethodInfo[]> use)
	{
		var context = new AssemblyLoadContext("AotWrapperSeeds", isCollectible: true);

		context.Resolving += (context, name) =>
		{
			string path = Path.Combine(referenceDirectory, name.Name + ".dll");
			return File.Exists(path) ? context.LoadFromStream(new MemoryStream(File.ReadAllBytes(path))) : null;
		};

		try
		{
			var assembly = context.LoadFromStream(new MemoryStream(File.ReadAllBytes(seedAssembly)));
			var seeds = assembly.GetType(SeedAssemblyName + "." + SeedAssemblyWriter.SeedsTypeName, throwOnError: true)!;
			var flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Instance
				| System.Reflection.BindingFlags.DeclaredOnly;
			var methods = seeds.GetMethods(flags);

			Assert.IsTrue(methods.Length > 0);
			use(methods);
		}
		finally
		{
			context.Unload();
		}
	}

	// Instances that only differ in reference type arguments have the same signatures: the seeding visits one of them.
	[TestMethod]
	public void When_Code_Names_Instances_Over_Several_Reference_Types_Then_One_Is_Visited()
	{
		string directory = Path.Combine(_outputDirectory, "references");
		var one = Collect(WriteHolderAssembly(Path.Combine(directory, "one"), 1));
		var three = Collect(WriteHolderAssembly(Path.Combine(directory, "three"), 3));

		Assert.AreEqual(one.Collected.ConstructedTypes, three.Collected.ConstructedTypes);
		Assert.AreEqual(one.Collected.VirtualMethods, three.Collected.VirtualMethods);
		Assert.AreEqual(one.Seeds.Count, three.Seeds.Count);
	}

	// The runtime shares the code of the instances of a generic method that only differ in reference type arguments: the seeding follows one.
	[TestMethod]
	public void When_Code_Calls_A_Generic_Method_Over_Several_Reference_Types_Then_One_Instance_Is_Followed()
	{
		var collector = Collect(WriteCallerAssembly(Path.Combine(_outputDirectory, "callers"), 3));

		Assert.AreEqual(1, collector.FlaggedMethodInstances.Count(i => i.Definition.Name == "Take"));
	}

	// Callers.Take<T>([Optional] T, int x 8), and Callers.Use, which calls Take<C0> ... Take<C(n-1)>, over the classes C0, C1, ...: the collector
	// lists the instances it follows, as T has flags (FlaggedMethodInstances).
	private static string WriteCallerAssembly(string directory, int instanceCount)
	{
		Directory.CreateDirectory(directory);
		const string Name = "AotSeedingCallers";
		using var assembly = AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition(Name, new Version(1, 0, 0, 0)), Name, ModuleKind.Dll);
		var module = assembly.MainModule;

		var callers = new TypeDefinition("AotSeedingCallers", "Callers", TypeAttributes.Public | TypeAttributes.Abstract, module.TypeSystem.Object);
		var take = new MethodDefinition("Take", MethodAttributes.Public | MethodAttributes.Static, module.TypeSystem.Void);
		var parameter = new GenericParameter("T", take);
		take.GenericParameters.Add(parameter);
		take.Parameters.Add(new ParameterDefinition("value", ParameterAttributes.Optional, parameter));

		for (int i = 0; i < 8; i++)
		{
			take.Parameters.Add(new ParameterDefinition("p" + i, ParameterAttributes.None, module.TypeSystem.Int32));
		}

		take.Body.GetILProcessor().Emit(OpCodes.Ret);
		callers.Methods.Add(take);

		var use = new MethodDefinition("Use", MethodAttributes.Public | MethodAttributes.Static, module.TypeSystem.Void);
		var il = use.Body.GetILProcessor();

		for (int i = 0; i < instanceCount; i++)
		{
			var argument = new TypeDefinition("AotSeedingCallers", "C" + i, TypeAttributes.Public, module.TypeSystem.Object);
			module.Types.Add(argument);

			var call = new GenericInstanceMethod(take);
			call.GenericArguments.Add(argument);
			il.Emit(OpCodes.Ldnull);

			for (int j = 0; j < 8; j++)
			{
				il.Emit(OpCodes.Ldc_I4_0);
			}

			il.Emit(OpCodes.Call, call);
		}

		il.Emit(OpCodes.Ret);
		callers.Methods.Add(use);
		module.Types.Add(callers);

		string path = Path.Combine(directory, Name + ".dll");
		assembly.Write(path);
		return path;
	}

	// Collects the signatures of one assembly, resolving the others in the runtime's folder.
	private static SignatureCollector Collect(string path)
	{
		using var resolver = new DefaultAssemblyResolver();
		resolver.AddSearchDirectory(Path.GetDirectoryName(typeof(object).Assembly.Location)!);
		using var module = ModuleDefinition.ReadModule(path, new ReaderParameters { AssemblyResolver = resolver });

		var collector = new SignatureCollector();
		collector.AddModule(module);
		return collector;
	}

	// Holder<T> with a virtual Put (T, int), and a method that names Holder<C0> ... Holder<C(n-1)>, over the classes C0, C1, ...
	private static string WriteHolderAssembly(string directory, int instanceCount)
	{
		Directory.CreateDirectory(directory);
		const string Name = "AotSeedingHolders";
		using var assembly = AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition(Name, new Version(1, 0, 0, 0)), Name, ModuleKind.Dll);
		var module = assembly.MainModule;

		var holder = new TypeDefinition("AotSeedingHolders", "Holder`1", TypeAttributes.Public, module.TypeSystem.Object);
		var parameter = new GenericParameter("T", holder);
		holder.GenericParameters.Add(parameter);
		var put = new MethodDefinition("Put", MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.NewSlot, module.TypeSystem.Void);
		put.Parameters.Add(new ParameterDefinition("value", ParameterAttributes.None, parameter));
		put.Parameters.Add(new ParameterDefinition("count", ParameterAttributes.None, module.TypeSystem.Int32));
		put.Body.GetILProcessor().Emit(OpCodes.Ret);
		holder.Methods.Add(put);
		module.Types.Add(holder);

		var user = new TypeDefinition("AotSeedingHolders", "User", TypeAttributes.Public | TypeAttributes.Abstract, module.TypeSystem.Object);
		var use = new MethodDefinition("Use", MethodAttributes.Public | MethodAttributes.Static, module.TypeSystem.Void);
		var il = use.Body.GetILProcessor();

		for (int i = 0; i < instanceCount; i++)
		{
			var argument = new TypeDefinition("AotSeedingHolders", "C" + i, TypeAttributes.Public, module.TypeSystem.Object);
			module.Types.Add(argument);

			var instance = new GenericInstanceType(holder);
			instance.GenericArguments.Add(argument);
			il.Emit(OpCodes.Ldtoken, instance);
			il.Emit(OpCodes.Pop);
		}

		il.Emit(OpCodes.Ret);
		user.Methods.Add(use);
		module.Types.Add(user);

		string path = Path.Combine(directory, Name + ".dll");
		assembly.Write(path);
		return path;
	}

	// AotSeedingShapes.Use: AotSeedingShapeHolder<AotSeedingWide<...>> has the shape of AotSeedingShapeHolder<string>, whose Count creates the
	// store over KeyValuePair<string, AotSeedingShapeKey>. The runtime shares the code of both, so the size of the large one is the small one's.
	[TestMethod]
	public void When_An_Instance_Is_Only_Large_In_Reference_Type_Arguments_Then_Its_Shape_Is_Followed()
	{
		var pair = new GenericInstanceType(_testModule.ImportReference(typeof(KeyValuePair<,>)));
		pair.GenericArguments.Add(_testModule.TypeSystem.Object);
		pair.GenericArguments.Add(_testModule.ImportReference(typeof(AotSeedingShapeKey)));
		var int32 = _testModule.TypeSystem.Int32;

		AssertExpected(WrapperSignature.GetInterpInWrapper(true, pair, [pair, .. Enumerable.Repeat(int32, 8)])!.Value.Name, "AotSeedingShapeHolder<T>.Count");
		Assert.IsFalse(_result.Limits.Any(l => l.Contains("AotSeedingShapeHolder")), string.Join(Environment.NewLine, _result.Limits));
	}

	// AotSeedingWideValues.Use: AotSeedingShapeHolder<AotSeedingWideValue<...>>, its constructor and its argument hold more than 64 types, and the
	// code that uses them is reported once. No other sample reaches a limit.
	[TestMethod]
	public void When_Instances_Hold_Too_Many_Types_Then_The_Code_That_Uses_Them_Is_Reported_Once()
	{
		const string Limit = "the generic instances that Uno.Wasm.Bootstrap.UnitTests.AotSeedingWideValues::Use() [Uno.Wasm.Bootstrap.UnitTests] uses "
			+ "whose generic arguments hold more than 64 types";

		CollectionAssert.AreEqual(new[] { Limit }, _result.Limits.ToList());
	}

	[TestMethod]
	public void When_Generating_Then_Recursive_Generic_Instances_Stop_At_The_AOT_Compiler_Depth()
	{
		var targets = _seeds.Methods;

		// The Take (AotSeedingRecursive<T>, T, sbyte, sbyte, sbyte, sbyte) methods of AotSeedingRecursive<int>, AotSeedingRecursive<ValueTuple<int>>,
		// ... up to 8 nested generic types, in which T nests up to 7.
		var nestings = targets
			.Where(m => m.HasThis && m.Parameters.Count == 6 && m.Parameters.Skip(2).All(p => p.ParameterType.MetadataType == MetadataType.SByte))
			.Select(m => SignatureCollector.GetGenericNesting(m.Parameters[1].ParameterType))
			.OrderBy(n => n);

		CollectionAssert.AreEqual(Enumerable.Range(0, 8).ToList(), nestings.ToList());
	}

	// AotSeedingGenericService<string>.Process: generic instances over reference types share their code, but the runtime enters the interpreter
	// with the signature of the instance's method, in which T is a reference.
	[TestMethod]
	public void When_Generating_Then_Generic_Instances_Over_Reference_Types_Are_Seeded()
	{
		const string Wrapper = "wrapper_other_object_interp_in_generic_intptr_double_intptr_double_intptr_double_intptr_double_intptr";
		AssertExpected(Wrapper);
	}

	// IAotSeedingLookup<AotSeedingKey> is only named by a call site: no code creates the class that implements it. Building the IMT slot of
	// Count resolves Find too.
	[TestMethod]
	public void When_Only_A_Call_Site_Names_An_Interface_Instance_Then_Its_Methods_Are_Seeded()
	{
		AssertInterpInWrapperIsExpected(typeof(IAotSeedingLookup<>), "Find", "IAotSeedingLookup<AotSeedingKey>.Find");
	}

	// AotSeedingRepository<AotSeedingKey> is only created inside the generic AotSeedingRepositoryFactory.Create<T>, and no code names its other
	// interface, IAotSeedingFinder<AotSeedingKey>.
	[TestMethod]
	public void When_Generic_Code_Creates_An_Instance_Then_Its_Interfaces_Are_Seeded()
	{
		AssertInterpInWrapperIsExpected(typeof(IAotSeedingFinder<>), "Find", "IAotSeedingFinder<AotSeedingKey>.Find");
	}

	// AotSeedingGenericDelegates.Create<T> creates a delegate to Handle<T>, called with AotSeedingKey from non-generic code.
	[TestMethod]
	public void When_Generic_Code_Creates_A_Delegate_Then_Its_Target_Is_Seeded()
	{
		AssertInterpInWrapperIsExpected(typeof(AotSeedingGenericDelegates), "Handle", "AotSeedingGenericDelegates.Handle<AotSeedingKey>");
	}

	// AotSeedingKeyComparer.Compare overrides AotSeedingComparer<AotSeedingKey>.Compare (T, T, ...): code compiled for any value type calls it
	// with a signature that has T rather than AotSeedingKey.
	[TestMethod]
	public void When_A_Type_Overrides_A_Virtual_Method_Of_A_Generic_Base_Then_The_Instance_Signature_Is_Seeded()
	{
		AssertInterpInWrapperIsExpected(typeof(AotSeedingComparer<>), "Compare", "AotSeedingComparer<AotSeedingKey>.Compare");
	}

	// Call sites in compiled code name the signatures of other virtual methods, and the AOT compiler emits their wrappers.
	[TestMethod]
	public void When_A_Virtual_Method_Has_No_Generic_Parameters_Then_It_Is_Not_Seeded()
	{
		AssertInterpInWrapperIsNotExpected(typeof(AotSeedingVirtualSample), "Describe");
	}

	// Delegates enter the interpreter through their targets, not through Invoke, BeginInvoke or EndInvoke.
	[TestMethod]
	[DataRow("Invoke")]
	[DataRow("BeginInvoke")]
	public void When_A_Delegate_Type_Is_Collected_Then_Its_Methods_Are_Not_Seeded(string methodName)
	{
		AssertInterpInWrapperIsNotExpected(typeof(AotSeedingCallback), methodName);
	}

	// Only virtual interface methods are in IMT slots.
	[TestMethod]
	public void When_An_Interface_Has_A_Non_Virtual_Method_Then_It_Is_Not_Seeded()
	{
		AssertInterpInWrapperIsNotExpected(typeof(IAotSeedingWithHelper), "Format");
	}

	private static void AssertInterpInWrapperIsNotExpected(Type type, string methodName)
	{
		var method = _testModule.ImportReference(type).Resolve().Methods.Single(m => m.Name == methodName);
		var parameterTypes = method.Parameters.Select(p => p.ParameterType).ToList();
		string wrapper = WrapperSignature.GetInterpInWrapper(method.HasThis, method.ReturnType, parameterTypes)!.Value.Name;

		Assert.IsFalse(_expectedNames.Contains(wrapper), $"{wrapper} ({type.Name}.{methodName}) is among the expected wrappers.");
	}

	// The runtime passes bool as byte and char as ushort, and enums as their underlying type: one seed covers each group.
	[TestMethod]
	[DataRow("System.Boolean", "System.Byte", "Uno.Wasm.Bootstrap.UnitTests.AotSeedingByteKind")]
	[DataRow("System.Char", "System.UInt16", "Uno.Wasm.Bootstrap.UnitTests.AotSeedingUShortKind")]
	public void When_Signatures_Only_Differ_In_Types_The_Runtime_Passes_Alike_Then_One_Is_Seeded(string a, string b, string c)
	{
		var targets = _seeds.Methods
			.Where(m => m.Parameters.Count == 3 && m.Parameters.Skip(1).All(p => p.ParameterType.FullName == "System.Decimal"))
			.Where(m => m.Parameters[0].ParameterType.FullName is var first && (first == a || first == b || first == c))
			.ToList();

		Assert.AreEqual(1, targets.Count, string.Join(", ", targets.Select(t => t.Parameters[0].ParameterType.FullName)));
	}

	// get_wrapper_shared_type_full gives the same wrapper to generic value types that only differ in reference type arguments.
	[TestMethod]
	public void When_Generic_Value_Types_Only_Differ_In_Reference_Type_Arguments_Then_One_Is_Seeded()
	{
		var targets = _seeds.Methods
			.Where(m => m.Parameters.Count == 4 && m.Parameters.Skip(1).All(p => p.ParameterType.FullName == "System.Decimal"))
			.Where(m => m.Parameters[0].ParameterType is GenericInstanceType { ElementType.Name: "KeyValuePair`2" })
			.ToList();

		Assert.AreEqual(1, targets.Count, string.Join(", ", targets.Select(t => t.Parameters[0].ParameterType.FullName)));
	}

	// The interp_in wrapper of a method whose generic parameter (of its type or its own) is AotSeedingKey.
	private static void AssertInterpInWrapperIsExpected(Type type, string methodName, string description)
	{
		var method = _testModule.ImportReference(type).Resolve().Methods.Single(m => m.Name == methodName);
		var key = _testModule.ImportReference(typeof(AotSeedingKey));
		var parameterTypes = method.Parameters.Select(p => p.ParameterType is GenericParameter ? key : p.ParameterType).ToList();
		var returnType = method.ReturnType is GenericParameter ? key : method.ReturnType;
		string wrapper = WrapperSignature.GetInterpInWrapper(method.HasThis, returnType, parameterTypes)!.Value.Name;

		AssertExpected(wrapper, description);
	}

	// The methods that need the wrapper, among the twins' expected wrappers.
	private static List<string> GetTwinOrigins(string wrapper)
		=> File.ReadAllLines(_twinFiles.ExpectedWrappers).Select(l => l.Split('\t')).Where(p => p[0] == wrapper).Select(p => p[1]).ToList();

	// The runtime has a wrapper for each of the two signatures, as their Value types differ; the name is the same, so the AOT compiler suffixes one.
	[TestMethod]
	public void When_Same_Named_Value_Types_Of_Two_Assemblies_Are_Seeded_Then_Each_Signature_Is()
	{
		const string Wrapper = "wrapper_other_object_interp_in_generic_AotSeedingTwins_Value_intptr_intptr_intptr_intptr_intptr_intptr_intptr_intptr";
		var origins = GetTwinOrigins(Wrapper);

		CollectionAssert.AreEquivalent(
			new[] { "AotSeedingTwins.IConsumer::Take [AotSeedingTwinA]", "AotSeedingTwins.IStore::Take [AotSeedingTwinB]" }, origins);
	}

	[TestMethod]
	public void When_Same_Named_Types_Of_Two_Assemblies_Are_Collected_Then_Both_Are()
	{
		const string Wrapper = "wrapper_other_object_interp_in_generic_double_long_double_long_double_long_double_long_double";
		var origins = GetTwinOrigins(Wrapper);

		Assert.IsTrue(origins.Any(o => o.EndsWith(" [AotSeedingTwinB]", StringComparison.Ordinal)), $"{Wrapper} is not among the expected wrappers.");
	}

	// Put (Value) of AotSeedingTwinA and Put (Value, int) of AotSeedingTwinB are called from the same seed method.
	[TestMethod]
	public void When_Same_Named_Value_Types_Of_Two_Assemblies_Are_Seeded_Then_The_Calls_Pass_Locals_Of_Their_Types()
	{
		using var assembly = AssemblyDefinition.ReadAssembly(_twinFiles.Assembly);
		var seeds = GetSeedsType(assembly);
		var calls = 0;

		foreach (var method in seeds.Methods.Where(m => m.Name.StartsWith("S", StringComparison.Ordinal)))
		{
			var locals = new List<VariableDefinition>();

			foreach (var instruction in method.Body.Instructions)
			{
				if (instruction.Operand is VariableDefinition local)
				{
					locals.Add(local);
				}
				else if (instruction.Operand is MethodReference target)
				{
					var parameterTypes = target.Parameters.Select(p => p.ParameterType is ByReferenceType r ? r.ElementType : p.ParameterType).ToList();

					foreach (var argument in locals)
					{
						Assert.IsTrue(
							parameterTypes.Any(t => t.FullName == argument.VariableType.FullName && t.Scope.Name == argument.VariableType.Scope.Name),
							$"{target.Name} is passed a local of type [{argument.VariableType.Scope.Name}]{argument.VariableType.FullName}.");
					}

					calls += target.Parameters.Any(p => p.ParameterType.FullName == TwinNamespace + ".Value") ? 1 : 0;
					locals.Clear();
				}
			}
		}

		Assert.AreEqual(4, calls);
	}

	[TestMethod]
	public void When_Generating_Then_The_Seed_Assembly_References_Defining_Assemblies()
	{
		var references = _seedAssembly.MainModule.AssemblyReferences.Select(r => r.Name).ToList();

		CollectionAssert.Contains(references, "System.Private.CoreLib");
		CollectionAssert.DoesNotContain(references, "mscorlib");
		CollectionAssert.DoesNotContain(references, "System.Runtime");
		CollectionAssert.DoesNotContain(references, "netstandard");
	}

	// The runtime passes all references alike.
	[TestMethod]
	public void When_Generating_Then_The_Targets_Take_And_Return_References_As_Object()
	{
		var targets = _seeds.Methods
			.Where(m => m.Name.StartsWith("M", StringComparison.Ordinal))
			.ToList();

		var references = targets
			.SelectMany(m => m.Parameters.Select(p => p.ParameterType).Prepend(m.ReturnType))
			.Select(t => t is ByReferenceType byRef ? byRef.ElementType : t)
			.Where(t => t is not (PointerType or FunctionPointerType) && !t.IsValueType && t.MetadataType != MetadataType.Void)
			.Select(t => t.FullName)
			.Distinct()
			.ToList();

		Assert.IsTrue(targets.Any(m => m.Parameters.Any(p => p.ParameterType.MetadataType == MetadataType.Object)));
		CollectionAssert.AreEqual(new[] { "System.Object" }, references);
	}

	[TestMethod]
	public void When_Generating_Then_The_Seed_Methods_Call_Uncompiled_Targets()
	{
		var seedMethods = _seeds.Methods.Where(m => m.Name.StartsWith("S", StringComparison.Ordinal)).ToList();
		var targets = _seeds.Methods.Where(m => m.Name.StartsWith("M", StringComparison.Ordinal)).ToList();

		Assert.AreEqual(_result.SeedMethodCount, seedMethods.Count);
		Assert.AreEqual(_result.SeedCount, targets.Count);
		Assert.IsTrue(seedMethods.All(m => m.IsStatic && m.Parameters.Count == 0 && m.ReturnType.FullName == "System.Void"));
		Assert.IsTrue(targets.All(m => m.ImplAttributes.HasFlag(MethodImplAttributes.NoInlining)));

		// Each target is called once, by a seed method.
		var called = seedMethods
			.SelectMany(m => m.Body.Instructions)
			.Where(i => i.OpCode == OpCodes.Call)
			.Select(i => ((MethodReference)i.Operand).Name)
			.OrderBy(n => n, StringComparer.Ordinal)
			.ToList();

		CollectionAssert.AreEqual(targets.Select(m => m.Name).OrderBy(n => n, StringComparer.Ordinal).ToList(), called);

		// Primitives are element types, as the C# compiler writes them.
		var intParameter = targets.SelectMany(m => m.Parameters).First(p => p.ParameterType.FullName == "System.Int32");
		Assert.AreEqual(MetadataType.Int32, intParameter.ParameterType.MetadataType);
	}

	// Cecil stamps a new module with the current time, in seconds, which the deterministic module version ID hashes too: generating again later
	// would give other bytes.
	[TestMethod]
	public void When_Generating_Then_The_Seed_Assembly_Has_No_Timestamp()
	{
		using var reader = new System.Reflection.PortableExecutable.PEReader(new MemoryStream(File.ReadAllBytes(_files.Assembly)));

		Assert.AreEqual(0, reader.PEHeaders.CoffHeader.TimeDateStamp);
	}

	// The AOT compiler compiles all the assemblies again when one of its inputs is newer than its output, so identical files aren't written
	// again. The first method found of a signature gives its seed its types: bool for Mark when IConsumer comes first, byte when IStore does.
	[TestMethod]
	public void When_Generating_Again_From_The_Inputs_In_Another_Order_Then_No_File_Is_Written()
	{
		var files = TestSeedFiles.In(Path.Combine(_outputDirectory, "twins", "again"));
		Directory.CreateDirectory(Path.GetDirectoryName(files.Assembly)!);
		var past = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
		string[] paths = [files.Assembly, files.Profile, files.ExpectedWrappers];

		foreach (var (from, to) in new[] { _twinFiles.Assembly, _twinFiles.Profile, _twinFiles.ExpectedWrappers }.Zip(paths))
		{
			File.Copy(from, to);
			File.SetLastWriteTimeUtc(to, past);
		}

		AotWrapperSeedGenerator.Generate(Enumerable.Reverse(_twinAssemblies), files, TwinSeedsPerMethod);

		foreach (string path in paths)
		{
			Assert.AreEqual(past, File.GetLastWriteTimeUtc(path), $"{Path.GetFileName(path)} was written again.");
		}
	}

	[TestMethod]
	public void When_Generating_Then_The_Profile_Lists_The_Seed_Methods()
	{
		Mono.Profiler.Aot.ProfileData profile;

		using (var stream = File.OpenRead(_files.Profile))
		{
			profile = new Mono.Profiler.Aot.ProfileReader().ReadAllData(stream);
		}

		Assert.AreEqual(_result.SeedMethodCount, profile.Methods.Length);
		Assert.IsTrue(profile.Methods.All(m => m.Type.Name == SeedAssemblyName + ".Seeds" && m.Signature == "void()" && m.ParamCount == 0));
		Assert.IsTrue(profile.Methods.All(m => m.Type.Module.Name == SeedAssemblyName));
	}

	// The interp_in wrapper of IAotSeedingStore<TKey>.Find (TKey, int x 8), which only the instance over TKey that a sample creates needs: the
	// samples create the store over each key in a different way.
	private static void AssertStoreIsSeeded<TKey>(string how) => AssertStoreIsSeeded(typeof(TKey), how);

	private static void AssertStoreIsSeeded(Type keyType, string how)
	{
		var key = _testModule.ImportReference(keyType);
		var int32 = _testModule.TypeSystem.Int32;
		AssertExpected(WrapperSignature.GetInterpInWrapper(true, key, [key, .. Enumerable.Repeat(int32, 8)])!.Value.Name, how);
	}

	// AotSeedingReachedStores.Use and AotSeedingGenericVirtualStores.Use: generic code that the runtime reaches without a call that names the instance,
	// or through calls that generic code makes, creates the store over each key.
	[TestMethod]
	[DataRow(typeof(AotSeedingConstructorKey), DisplayName = "In the parameterless constructor that new T() runs")]
	[DataRow(typeof(AotSeedingFieldInitializerKey), DisplayName = "In an instance field initializer, which new T() runs")]
	[DataRow(typeof(AotSeedingLazyKey), DisplayName = "In the parameterless constructor that new Lazy<T>() runs")]
	[DataRow(typeof(AotSeedingStaticAbstractKey), DisplayName = "In the implementation of a static abstract member")]
	[DataRow(typeof(AotSeedingStaticDefaultKey), DisplayName = "In the default implementation of a static virtual member")]
	[DataRow(typeof(AotSeedingAsyncHostKey), DisplayName = "In an async method of a generic class")]
	[DataRow(typeof(AotSeedingAsyncLambdaKey), DisplayName = "In an async lambda of a generic method")]
	[DataRow(typeof(AotSeedingAsyncIteratorKey), DisplayName = "In a generic async iterator")]
	[DataRow(typeof(AotSeedingBaseOverrideKey), DisplayName = "In an override in a generic base class")]
	[DataRow(typeof(AotSeedingExplicitKey), DisplayName = "In an explicit interface implementation in a generic class")]
	[DataRow(typeof(AotSeedingInheritedKey), DisplayName = "In an interface implementation that a closed class inherits from a generic one")]
	[DataRow(typeof(AotSeedingClosureKey), DisplayName = "In a lambda that captures a variable")]
	[DataRow(typeof(AotSeedingStaticLambdaKey), DisplayName = "In a static lambda")]
	[DataRow(typeof(AotSeedingLocalFunctionKey), DisplayName = "In a static local function")]
	[DataRow(typeof(AotSeedingCapturingLocalFunctionKey), DisplayName = "In a local function that captures a variable")]
	[DataRow(typeof(AotSeedingLazyFactoryKey), DisplayName = "In the factory of a Lazy<T>")]
	[DataRow(typeof(AotSeedingFactoryKey), DisplayName = "In a Func<T> factory")]
	[DataRow(typeof(AotSeedingLinqKey), DisplayName = "In a LINQ Select")]
	[DataRow(typeof(AotSeedingConstrainedKey), DisplayName = "In a generic struct called through a constrained call")]
	[DataRow(typeof(AotSeedingRecordClassKey), DisplayName = "In a virtual method of a generic record")]
	[DataRow(typeof(AotSeedingGenericInterfaceMethodKey), DisplayName = "In the implementation of a generic interface method")]
	[DataRow(typeof(AotSeedingGenericAbstractMethodKey), DisplayName = "In the override of an abstract generic method")]
	[DataRow(typeof(AotSeedingGenericOverrideKey), DisplayName = "In the override of a virtual generic method")]
	[DataRow(typeof(AotSeedingGenericMethodDelegateKey), DisplayName = "In the implementation of a generic interface method, through a delegate")]
	[DataRow(typeof(AotSeedingGenericStaticAbstractKey), DisplayName = "In the implementation of a generic static abstract member")]
	[DataRow(typeof(AotSeedingGenericDefaultMethodKey), DisplayName = "In a generic default interface method")]
	[DataRow(typeof(AotSeedingGenericStaticDefaultKey), DisplayName = "In the default implementation of a generic static virtual member")]
	[DataRow(typeof(AotSeedingExplicitActivatorKey), DisplayName = "In the constructor that Activator.CreateInstance<T>() runs")]
	[DataRow(typeof(AotSeedingStructConstructorKey), DisplayName = "In the parameterless constructor of a generic struct, which new T() runs")]
	[DataRow(typeof(AotSeedingInheritedStaticAbstractKey), DisplayName = "In a static abstract implementation inherited from a generic base")]
	[DataRow(typeof(AotSeedingClassLambdaKey), DisplayName = "In a lambda of a generic class")]
	[DataRow(typeof(AotSeedingStaticFieldKey), DisplayName = "In a static field initializer that closed code reaches")]
	public void When_Generic_Code_The_Runtime_Reaches_Creates_An_Instance_Then_Its_Interfaces_Are_Seeded(Type key)
		=> AssertStoreIsSeeded(key, "AotSeedingReachedStores.Use or AotSeedingGenericVirtualStores.Use");

	[TestMethod]
	public void When_Generic_Code_That_Closed_Code_Calls_Creates_An_Instance_Then_Its_Interfaces_Are_Seeded()
		=> AssertStoreIsSeeded<AotSeedingDirectKey>("in a generic method");

	[TestMethod]
	public void When_A_Generic_Type_Argument_Is_Created_Then_Its_Interfaces_Are_Seeded()
		=> AssertStoreIsSeeded<AotSeedingActivatorKey>("as the type argument of new T()");

	[TestMethod]
	public void When_A_Record_Struct_Implements_A_Generic_Interface_Then_Its_Methods_Are_Seeded()
		=> AssertStoreIsSeeded<AotSeedingRecordKey>("by a record struct");

	[TestMethod]
	public void When_Generic_Code_Creates_A_Class_With_A_Default_Implementation_Then_It_Is_Seeded()
		=> AssertStoreIsSeeded<AotSeedingDefaultKey>("with a default implementation");

	// The body of an async method is in the MoveNext of its state machine, which the runtime reaches through IAsyncStateMachine.
	[TestMethod]
	public void When_A_Generic_Async_Method_Creates_An_Instance_Then_Its_Interfaces_Are_Seeded()
		=> AssertStoreIsSeeded<AotSeedingAsyncKey>("in a generic async method");

	// The body of an iterator is in the MoveNext of its class, which the runtime reaches through IEnumerator.
	[TestMethod]
	public void When_A_Generic_Iterator_Creates_An_Instance_Then_Its_Interfaces_Are_Seeded()
		=> AssertStoreIsSeeded<AotSeedingIteratorKey>("in a generic iterator");

	[TestMethod]
	public void When_An_Override_In_A_Generic_Class_Creates_An_Instance_Then_Its_Interfaces_Are_Seeded()
		=> AssertStoreIsSeeded<AotSeedingOverrideKey>("in an override in a generic class, called through its base class");

	[TestMethod]
	public void When_An_Interface_Implementation_In_A_Generic_Class_Creates_An_Instance_Then_Its_Interfaces_Are_Seeded()
		=> AssertStoreIsSeeded<AotSeedingImplementationKey>("in an interface implementation in a generic class, called through the interface");

	// The runtime runs static constructors when it initializes a type: nothing calls them.
	[TestMethod]
	public void When_A_Static_Field_Initializer_Of_A_Generic_Class_Creates_An_Instance_Then_Its_Interfaces_Are_Seeded()
		=> AssertStoreIsSeeded<AotSeedingInitializerKey>("in a static field initializer of a generic class");

	// Generic virtual methods aren't in IMT slots, but code compiled for any value type calls them with a signature that has T.
	[TestMethod]
	public void When_Generic_Code_Calls_A_Generic_Virtual_Method_Then_Its_Instance_Is_Seeded()
	{
		var key = _testModule.ImportReference(typeof(AotSeedingVisitKey));
		var int32 = _testModule.TypeSystem.Int32;
		AssertExpected(WrapperSignature.GetInterpInWrapper(true, key, [key, .. Enumerable.Repeat(int32, 8)])!.Value.Name, "AotSeedingVisitor.Visit<T>");
	}

	// Whatever the number of parameters: above 8, compiled code that calls the method aborts in interp.c; with 8, as the test app's variants showed,
	// the app stops with "function signature mismatch".
	[TestMethod]
	[DataRow("WithDefault(int, int, int, int, int, int, int, int, AotSeedingExplicitBox)", 9, "a default value", DisplayName = "9 parameters")]
	[DataRow("WithEightParameters(int, int, int, int, int, int, int, AotSeedingExplicitBox)", 8, "a default value", DisplayName = "8 parameters")]
	[DataRow("WithFewParameters(int, AotSeedingExplicitBox)", 2, "a default value", DisplayName = "2 parameters")]
	[DataRow("WithMarshalAs(int, AotSeedingExplicitBox)", 2, "a [MarshalAs] attribute", DisplayName = "[MarshalAs]")]
	[DataRow("WithInAndOut(int, AotSeedingExplicitBox)", 2, "[In] and [Out] attributes", DisplayName = "[In] and [Out]")]
	public void When_A_Struct_Parameter_Keeps_Its_Flags_Then_The_Method_Is_Unenterable(string method, int count, string flags)
	{
		string name = $"{typeof(AotSeedingAttributedParameters).FullName}::{method} [Uno.Wasm.Bootstrap.UnitTests]";
		var finding = _result.UnenterableMethods.SingleOrDefault(f => f.Method == name);

		Assert.IsNotNull(finding, $"{name} isn't reported: {string.Join(", ", _result.UnenterableMethods.Select(f => f.Method))}");
		Assert.AreEqual($"parameter {count} ('box')", finding.Parameter);
		Assert.AreEqual(flags, finding.Flags);
		Assert.AreEqual(typeof(AotSeedingExplicitBox).FullName, finding.Type);
		Assert.AreEqual(count, finding.ParameterCount);
		Assert.IsFalse(finding.IsVirtual);
		Assert.IsFalse(finding.IsFunctionPointer);
		Assert.IsNull(finding.TypeArgument);
	}

	// Passed with 'in', mapped to a Mono.ValueTuple, or without flags, the parameter doesn't keep them, and the flags of a return value don't
	// matter.
	[TestMethod]
	public void When_A_Struct_Parameter_Has_No_Flags_Or_The_Runtime_Replaces_Its_Type_Then_The_Method_Is_Not_Reported()
	{
		var reported = _result.UnenterableMethods.Where(f => f.Method.StartsWith(typeof(AotSeedingAttributedParameters).FullName + "::")).ToList();
		string[] unreported = ["::WithIn(", "::WithMappedType(", "::WithoutDefault(", "::WithMarshaledReturn(", "::WithMarshaledReturnAndNineParameters("];

		Assert.AreEqual(5, reported.Count, string.Join(Environment.NewLine, reported));
		Assert.IsFalse(reported.Any(f => unreported.Any(f.Method.Contains)), string.Join(Environment.NewLine, reported));
	}

	// DescribeBoth has a flagged parameter of an explicit layout struct, and one of T: one warning names the first.
	[TestMethod]
	public void When_A_Method_Has_Flagged_Parameters_Of_Both_Kinds_Then_It_Is_Reported_Once()
	{
		var findings = _result.UnenterableMethods.Where(f => f.Method.Contains("AotSeedingGenericDefaults<T>::DescribeBoth(")).ToList();

		Assert.AreEqual(1, findings.Count, string.Join(Environment.NewLine, findings));
		Assert.AreEqual("parameter 1 ('box')", findings[0].Parameter);
	}

	// The layout cache spares the walks of the value types that seeds share, and keeps how deep they went: AotSeedingDepth nested 40 times walks
	// alone, and nested 70 times goes deeper than the bound, whether the walk of the inner 40 levels was cached or not.
	[TestMethod]
	public void When_The_Layout_Cache_Spares_A_Walk_Then_The_Bounds_Apply_As_Without_It()
	{
		var depth = _testModule.ImportReference(typeof(AotSeedingDepth<>));
		TypeReference inner = _testModule.TypeSystem.Int32;

		for (int i = 0; i < 40; i++)
		{
			var instance = new GenericInstanceType(depth);
			instance.GenericArguments.Add(inner);
			inner = instance;
		}

		var outer = inner;

		for (int i = 0; i < 30; i++)
		{
			var instance = new GenericInstanceType(depth);
			instance.GenericArguments.Add(outer);
			outer = instance;
		}

		var uncached = (WrapperSignature.FindUnsharableStruct(inner), WrapperSignature.FindUnsharableStruct(outer), WrapperSignature.CachedLayoutCount);

		using (CecilTypes.BeginGeneration())
		{
			var innerRoot = WrapperSignature.FindUnsharableStruct(inner);
			int cached = WrapperSignature.CachedLayoutCount;
			var outerRoot = WrapperSignature.FindUnsharableStruct(outer);

			Assert.IsNull(uncached.Item1);
			Assert.AreSame(outer, uncached.Item2?.Root);
			Assert.AreEqual(0, uncached.CachedLayoutCount);
			Assert.IsNull(innerRoot);
			Assert.IsTrue(cached > 40, $"{cached} layouts are cached.");
			Assert.AreSame(outer, outerRoot?.Root);
		}
	}

	// inflate_generic_type in class.c gives the type argument the flags of a parameter of a generic type.
	[TestMethod]
	[DataRow("AotSeedingGenericDefaults<T>::Describe(int, int, int, int, int, int, int, int, T)", 9,
		DisplayName = "Parameter of a generic type's parameter type")]
	[DataRow("AotSeedingGenericMethodDefaults::Describe<T>(int, int, int, int, int, int, int, int, T)", 9,
		DisplayName = "Parameter of a generic method's parameter type")]
	[DataRow("AotSeedingGenericDefaults<T>::DescribeFew(int, T)", 2, DisplayName = "Parameter of a generic type's parameter type, 2 parameters")]
	[DataRow("AotSeedingGenericMethodDefaults::DescribeFew<T>(int, T)", 2, DisplayName = "Parameter of a generic method's parameter type, 2 parameters")]
	public void When_A_Generic_Parameter_Keeps_Its_Flags_With_Its_Type_Argument_Then_The_Method_Is_Unenterable(string method, int count)
	{
		string name = $"Uno.Wasm.Bootstrap.UnitTests.{method} [Uno.Wasm.Bootstrap.UnitTests]";
		var finding = _result.UnenterableMethods.SingleOrDefault(f => f.Method == name);

		Assert.IsNotNull(finding, $"{name} isn't reported: {string.Join(", ", _result.UnenterableMethods.Select(f => f.Method))}");
		Assert.AreEqual($"parameter {count} ('value')", finding.Parameter);
		Assert.AreEqual(typeof(AotSeedingDefaultsKey).FullName, finding.TypeArgument);
		Assert.AreEqual(count, finding.ParameterCount);
	}

	// The runtime's mapping of the type arguments and fields of these legal structs never ends: the AOT compiler would overflow its stack on their
	// seeds, which would fail the build. They're left out, and reported once each.
	[TestMethod]
	[DataRow(typeof(AotSeedingMemoryNode), DisplayName = "A struct with a ReadOnlyMemory<T> field over itself")]
	[DataRow(typeof(AotSeedingSegmentNode), DisplayName = "A struct with an ArraySegment<T> field over itself")]
	[DataRow(typeof(AotSeedingWrapperNode), DisplayName = "A struct with a field of a struct that wraps an array of it")]
	public void When_A_Struct_Holds_A_Struct_Over_Itself_Then_Its_Seeds_Are_Left_Out(Type type)
	{
		var unsharable = WrapperSignature.FindUnsharableStruct(_testModule.ImportReference(type));
		string structs = string.Join(Environment.NewLine, _result.UnprotectedStructs);

		Assert.AreEqual(type.FullName, unsharable?.Root.FullName);
		Assert.AreEqual(WrapperSignature.UnsharableReason.CyclicLayout, unsharable?.Reason);
		Assert.AreEqual(1, _result.UnprotectedStructs.Count(c => c.Type.Contains(type.Name)), structs);
		Assert.IsTrue(_result.UnprotectedStructs.Any(c => c.Type == type.FullName && c.Reason == WrapperSignature.UnsharableReason.CyclicLayout), structs);
		Assert.IsFalse(_seeds.Methods.SelectMany(m => m.Parameters).Any(p => p.ParameterType.FullName == type.FullName), $"{type.Name} is seeded.");
	}

	// The types that a cycle goes through, and the structs that hold them, have the struct that holds a struct over itself as their root, so that
	// UNOWASM010 names it once: ReadOnlyMemory<AotSeedingMemoryNode> holds AotSeedingMemoryNode through its type argument, and Memory<T> and Span<T>
	// hold the root of the cycle.
	[TestMethod]
	[DataRow(typeof(ReadOnlyMemory<AotSeedingMemoryNode>), typeof(AotSeedingMemoryNode))]
	[DataRow(typeof(Memory<AotSeedingMemoryNode>), typeof(AotSeedingMemoryNode))]
	[DataRow(typeof(Span<AotSeedingMemoryNode>), typeof(AotSeedingMemoryNode))]
	[DataRow(typeof(ArraySegment<AotSeedingSegmentNode>), typeof(AotSeedingSegmentNode))]
	[DataRow(typeof(AotSeedingArrayWrapper<AotSeedingWrapperNode>), typeof(AotSeedingWrapperNode))]
	public void When_A_Type_Goes_Through_A_Cycle_Then_Its_Root_Is_The_Struct_That_Holds_A_Struct_Over_Itself(Type type, Type root)
	{
		Assert.AreEqual(root.FullName, WrapperSignature.FindUnsharableStruct(_testModule.ImportReference(type))?.Root.FullName);
	}

	// IAotSeedingTrees takes ReadOnlyMemory<AotSeedingMemoryNode>, AotSeedingGenericNode<int> and AotSeedingGenericNode<long>: one warning names
	// AotSeedingMemoryNode, and one the generic struct, for all its instances.
	[TestMethod]
	public void When_Several_Types_Go_Through_A_Cycle_Then_UNOWASM010_Names_Its_Root_Once()
	{
		string layouts = string.Join(Environment.NewLine, _result.UnprotectedStructs);

		Assert.IsFalse(_result.UnprotectedStructs.Any(c => c.Type.StartsWith("System.", StringComparison.Ordinal)), layouts);
		Assert.AreEqual(1, _result.UnprotectedStructs.Count(c => c.Type.Contains(nameof(AotSeedingMemoryNode))), layouts);
		Assert.AreEqual(1, _result.UnprotectedStructs.Count(c => c.Type == "Uno.Wasm.Bootstrap.UnitTests.AotSeedingGenericNode<T>"), layouts);
		Assert.IsFalse(_result.UnprotectedStructs.Any(c => c.Type.Contains("AotSeedingGenericNode<int>") || c.Type.Contains("<long>")), layouts);
	}

	// get_wrapper_shared_type_full asserts that a generic struct has fewer than 16 type arguments before it maps them, whether a type is the struct,
	// holds it in a field, or has it as a type argument: the struct is what the runtime can't map.
	[TestMethod]
	[DataRow(typeof(Wide16), DisplayName = "16 type arguments, directly")]
	[DataRow(typeof(AotSeedingWide16Holder), DisplayName = "16 type arguments, through a field")]
	[DataRow(typeof(AotSeedingDepth<Wide16>), DisplayName = "16 type arguments, through a type argument")]
	[DataRow(typeof(Wide16?), DisplayName = "16 type arguments, through Nullable<T>")]
	[DataRow(typeof(AotSeedingDepth<AotSeedingDepth<AotSeedingWide16Holder>>), DisplayName = "16 type arguments, through type arguments and a field")]
	public void When_A_Type_Reaches_A_Generic_Struct_With_16_Type_Arguments_Then_The_Runtime_Cannot_Map_It(Type type)
	{
		var unsharable = WrapperSignature.FindUnsharableStruct(_testModule.ImportReference(type));

		Assert.AreEqual(_testModule.ImportReference(typeof(Wide16)).FullName, unsharable?.Root.FullName);
		Assert.AreEqual(WrapperSignature.UnsharableReason.TooManyTypeArguments, unsharable?.Reason);
	}

	// With 15 type arguments, the runtime maps the struct; and it passes a class over a struct with 16, an array of it, or a reference to it,
	// without mapping the struct.
	[TestMethod]
	[DataRow(typeof(Wide15), DisplayName = "15 type arguments, directly")]
	[DataRow(typeof(AotSeedingWide15Holder), DisplayName = "15 type arguments, through a field")]
	[DataRow(typeof(AotSeedingDepth<Wide15>), DisplayName = "15 type arguments, through a type argument")]
	[DataRow(typeof(List<Wide16>), DisplayName = "A class over a struct with 16 type arguments")]
	[DataRow(typeof(Wide16[]), DisplayName = "An array of a struct with 16 type arguments")]
	public void When_A_Type_Reaches_No_Generic_Struct_With_16_Type_Arguments_Then_The_Runtime_Maps_It(Type type)
	{
		Assert.IsNull(WrapperSignature.FindUnsharableStruct(_testModule.ImportReference(type)));
		Assert.IsNull(WrapperSignature.FindUnsharableStruct(new ByReferenceType(_testModule.ImportReference(type))));
	}

	// IAotSeedingWideStructs: the seeds that take or return a generic struct with 16 type arguments, by value, directly, through a field or through a
	// type argument, would stop the AOT compiler. They're left out, and UNOWASM010 names the struct once. Those with 15 are seeded, with their
	// wrappers expected.
	[TestMethod]
	public void When_A_Signature_Reaches_A_Generic_Struct_With_16_Type_Arguments_Then_It_Is_Left_Out_And_UNOWASM010_Names_It()
	{
		var types = _seeds.Methods.SelectMany(m => m.Parameters.Select(p => p.ParameterType).Append(m.ReturnType)).Select(t => t.FullName).ToHashSet();
		string Name(Type type) => _testModule.ImportReference(type).FullName;

		foreach (var type in new[] { typeof(Wide16), typeof(AotSeedingWide16Holder), typeof(AotSeedingDepth<Wide16>) })
		{
			Assert.IsFalse(types.Contains(Name(type)), $"{Name(type)} is seeded.");
		}

		foreach (var type in new[] { typeof(Wide15), typeof(AotSeedingWide15Holder), typeof(AotSeedingDepth<Wide15>) })
		{
			Assert.IsTrue(types.Contains(Name(type)), $"{Name(type)} isn't seeded.");
		}

		var wide15 = _testModule.ImportReference(typeof(Wide15));
		AssertExpected(WrapperSignature.GetGsharedvtInWrapperName(true, _testModule.TypeSystem.Void, [wide15])!, "Take(Wide15)");

		string structs = string.Join(Environment.NewLine, _result.UnprotectedStructs);
		var reported = _result.UnprotectedStructs.Where(s => s.Type.Contains("AotSeedingWide")).ToList();
		string definition = $"Uno.Wasm.Bootstrap.UnitTests.AotSeedingWide16<{string.Join(", ", Enumerable.Range(1, 16).Select(i => $"T{i}"))}>";

		Assert.AreEqual(1, reported.Count, structs);
		Assert.AreEqual(definition, reported[0].Type);
		Assert.AreEqual(WrapperSignature.UnsharableReason.TooManyTypeArguments, reported[0].Reason);
		StringAssert.StartsWith(reported[0].Method, "Uno.Wasm.Bootstrap.UnitTests.IAotSeedingWideStructs::");
	}

	// CecilTypes.Inflate closes the function pointers of an instance's signature, as it closes other types, keeping their calling convention: the
	// interface method of IAotSeedingCallbacks<AotSeedingCallbackKey> takes a delegate*<AotSeedingCallbackKey, void>.
	[TestMethod]
	public void When_A_Signature_Of_A_Generic_Instance_Has_A_Function_Pointer_Then_It_Is_Seeded_Closed()
	{
		var key = _testModule.ImportReference(typeof(AotSeedingCallbackKey));
		var callback = new FunctionPointerType { ReturnType = _testModule.TypeSystem.Void };
		callback.Parameters.Add(new ParameterDefinition(key));
		var int64 = _testModule.TypeSystem.Int64;

		AssertExpected(WrapperSignature.GetInterpInWrapper(true, _testModule.TypeSystem.Void, [callback, .. Enumerable.Repeat(int64, 8)])!.Value.Name);
		Assert.IsTrue(
			_seeds.Methods.Any(m => m.Parameters.Count == 9 && m.Parameters[0].ParameterType is FunctionPointerType { Parameters: [var p] }
				&& p.ParameterType.FullName == typeof(AotSeedingCallbackKey).FullName),
			"delegate*<AotSeedingCallbackKey, void> isn't seeded.");
	}

	[TestMethod]
	public void When_Inflating_A_Function_Pointer_Then_Its_Types_Are_Closed_And_Its_Calling_Convention_Kept()
	{
		var take = _testModule.ImportReference(typeof(IAotSeedingCallbacks<>)).Resolve().Methods.Single(m => m.Name == "Take");
		var instance = new GenericInstanceType(take.DeclaringType);
		instance.GenericArguments.Add(_testModule.TypeSystem.Int32);

		var open = (FunctionPointerType)take.Parameters[0].ParameterType;
		var closed = (FunctionPointerType)CecilTypes.Inflate(open, instance, null);

		Assert.IsFalse(closed.ContainsGenericParameter);
		Assert.AreEqual("System.Int32", closed.Parameters.Single().ParameterType.FullName);
		Assert.AreEqual(open.CallingConvention, closed.CallingConvention);
		Assert.AreEqual("System.Void", closed.ReturnType.FullName);
	}

	// The build messages name types and methods as C# does.
	[TestMethod]
	public void When_Naming_A_Method_Then_Its_Types_Are_Named_As_In_CSharp()
	{
		var methods = _testModule.ImportReference(typeof(AotSeedingNames)).Resolve().Methods;

		Assert.AreEqual(
			"Uno.Wasm.Bootstrap.UnitTests.AotSeedingNames::Take<T>(in int, out long, int?, (int, string), AotSeedingOuter<string>.Inner<T>, "
				+ "delegate*<int, void>, List<int>[], T*) [Uno.Wasm.Bootstrap.UnitTests]",
			MemberNames.Method(methods.Single(m => m.Name == "Take")));
		Assert.AreEqual(
			"Uno.Wasm.Bootstrap.UnitTests.AotSeedingNames::Run.Local(int) [Uno.Wasm.Bootstrap.UnitTests]",
			MemberNames.Method(methods.Single(m => m.Name.Contains("g__Local"))));
	}

	[TestMethod]
	public void When_Parsing_llvm_nm_Output_Then_Only_Functions_Are_Kept()
	{
		string[] lines =
		[
			"-------- t aot_instances_aot_wrapper_gsharedvt_in_sig_i8_i8i8",
			"-------- d info_aot_instances_aot_wrapper_gsharedvt_in_sig_i8_i8i8",
			"00001234 T exported_function",
			"",
		];

		var symbols = lines.Select(AotWrapperCheck.ParseFunctionSymbol).Where(s => s != null).ToList();

		CollectionAssert.AreEqual(new[] { "aot_instances_aot_wrapper_gsharedvt_in_sig_i8_i8i8", "exported_function" }, symbols);
	}

	[TestMethod]
	public void When_Checking_Then_Missing_Wrappers_Are_Reported()
	{
		const string InterpIn = "wrapper_other_object_interp_in_generic_long_long_long_long_long_long_long_long_long";

		var result = AotWrapperCheck.Evaluate(
			[
				"gsharedvt_in_sig_i8_i8i8\tA::M [A]",
				"gsharedvt_in_sig_i8_this_i8i8\tA::N [A]",
				InterpIn + "\tB::M [B]",
				InterpIn + "\tB::N [B]",
			],
			[
				"aot_instances_aot_wrapper_gsharedvt_in_sig_i8_i8i8",
				"aot_instances_" + InterpIn,
				"aot_instances_" + InterpIn + "_long",
			]);

		Assert.AreEqual(2, result.GsharedvtInExpected);
		Assert.AreEqual(1, result.GsharedvtInFound);
		Assert.AreEqual(2, result.InterpInExpected);
		Assert.AreEqual(1, result.InterpInFound);

		// interp_in wrappers are reported first.
		Assert.AreEqual(2, result.MissingWrappers.Count);
		Assert.AreEqual(InterpIn, result.MissingWrappers[0].Name);
		Assert.AreEqual(1, result.MissingWrappers[0].Found);
		Assert.AreEqual("gsharedvt_in_sig_i8_this_i8i8", result.MissingWrappers[1].Name);
	}

	// mono_class_layout_fields raises the alignment of a value type to its packing (up to 8): 8 bytes aligned to 8 map to Mono.ValueTuple`1<long>.
	[TestMethod]
	public void When_Pack_Exceeds_The_Field_Alignment_Then_The_Wrapper_Name_Follows_The_Runtime()
	{
		Assert.AreEqual("gsharedvt_in_sig_void_this_cl1e_Mono_dValueTuple_601_3clong_3e_", GetGsharedvtInWrapperName<AotSeedingPackEight>());
	}

	// Fields with references are aligned to the pointer size even in packed value types: 8 bytes aligned to 4.
	[TestMethod]
	public void When_A_Packed_Value_Type_Has_A_Reference_Then_The_Wrapper_Name_Follows_The_Runtime()
	{
		Assert.AreEqual(
			"gsharedvt_in_sig_void_this_cl26_Mono_dValueTuple_602_3cint_2c_20int_3e_", GetGsharedvtInWrapperName<AotSeedingPackOneReference>());
	}

	// Two bytes packed to 4 are rounded up to 4 bytes aligned to 4.
	[TestMethod]
	public void When_Pack_Rounds_Up_A_Small_Value_Type_Then_The_Wrapper_Name_Follows_The_Runtime()
	{
		Assert.AreEqual("gsharedvt_in_sig_void_this_cl1d_Mono_dValueTuple_601_3cint_3e_", GetGsharedvtInWrapperName<AotSeedingPackFourBytes>());
	}

	// An explicit size isn't rounded up to the alignment: 6 bytes aligned to 4, which map to a single int.
	[TestMethod]
	public void When_A_Packed_Value_Type_Has_An_Explicit_Size_Then_It_Is_Not_Rounded_Up()
	{
		Assert.AreEqual("gsharedvt_in_sig_void_this_cl1d_Mono_dValueTuple_601_3cint_3e_", GetGsharedvtInWrapperName<AotSeedingPackSized>());
	}

	private static string? GetGsharedvtInWrapperName<T>()
		=> WrapperSignature.GetGsharedvtInWrapperName(true, _testModule.TypeSystem.Void, [_testModule.ImportReference(typeof(T))]);

	[TestMethod]
	public void When_Checking_Then_Suffixed_Interp_In_Wrappers_Count()
	{
		const string InterpIn = "wrapper_other_object_interp_in_generic_long_long_long_long_long_long_long_long_long";

		var result = AotWrapperCheck.Evaluate(
			[InterpIn + "\tB::M [B]", InterpIn + "\tB::N [B]"],
			["aot_instances_" + InterpIn, "aot_instances_" + InterpIn + "_0"]);

		Assert.AreEqual(2, result.InterpInFound);
		Assert.AreEqual(0, result.MissingWrappers.Count);
	}
}
