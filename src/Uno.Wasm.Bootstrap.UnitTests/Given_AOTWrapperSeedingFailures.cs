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
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using Microsoft.Build.Utilities;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Uno.Wasm.Bootstrap.AOTWrapperSeeding;

namespace Uno.Wasm.Bootstrap.UnitTests;

/// <summary>
/// Inputs the seeding can only partly handle, in assemblies written with Mono.Cecil, which one run of the task goes through.
/// </summary>
[TestClass]
public class Given_AOTWrapperSeedingFailures
{
	private const string SeedAssemblyName = TestSeedFiles.AssemblyName;
	private const string Namespace = "AotSeedingFailures";

	private static string _directory = "";
	private static string _unreadable = "";
	private static string _fanOut = "";
	private static AotWrapperSeedGenerator.SeedFiles _files = null!;
	private static RecordingBuildEngine _engine = null!;
	private static GenerateAOTWrapperSeedsTask _task = null!;

	[ClassInitialize]
	public static void Initialize(TestContext context)
	{
		_directory = Path.Combine(Path.GetTempPath(), "uno-aot-wrapper-seeding-failures-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(_directory);

		// A folder with the name of an assembly, which can't be read.
		_unreadable = Path.Combine(_directory, "AotSeedingUnreadable.dll");
		Directory.CreateDirectory(_unreadable);

		_files = TestSeedFiles.In(Path.Combine(_directory, "seeds"));
		_engine = new RecordingBuildEngine();
		_task = CreateTask(_engine, _files, WriteHolder(), WriteBroken(), WriteUnnamed(), WriteRecursion(), WriteLayouts(), WriteModuleScope(),
			WriteContainment(), _unreadable, CoreLibrary);
		_task.Execute();

		// Not an input of the task run: the tests collect it with lower limits.
		_fanOut = WriteFanOut();
	}

	[ClassCleanup]
	public static void Cleanup()
	{
		try
		{
			Directory.Delete(_directory, true);
		}
		catch (Exception e) when (e is IOException or UnauthorizedAccessException)
		{
		}
	}

	private static string CoreLibrary => typeof(object).Assembly.Location;

	[TestMethod]
	public void When_Some_Inputs_Cannot_Be_Handled_Then_The_Others_Are_Seeded()
	{
		Assert.IsTrue(_task.Generated, string.Join(Environment.NewLine, _engine.Warnings.Select(w => w.Message)));
		Assert.IsFalse(_engine.Warnings.Any(w => w.Code == "UNOWASM008"));
	}

	// Holder has a field of a type whose assembly isn't among the inputs, so its wrapper names can't be predicted. The AOT compiler gets the
	// assembly through MONO_PATH when the app excludes it from the AOT compilation.
	[TestMethod]
	public void When_A_Wrapper_Name_Cannot_Be_Predicted_Then_The_Signature_Is_Still_Seeded()
	{
		Assert.IsTrue(GetTargets().SelectMany(m => m.Parameters).Any(p => p.ParameterType.FullName == $"{Namespace}.Holder"), "Holder isn't seeded.");
		AssertLogged($"the check doesn't look for the wrapper of {Namespace}.IService::Take [AotSeedingHolder]");
	}

	// The signature of IBroken.Run has an element type that doesn't exist, which the reader can't read. Only IBroken is skipped.
	[TestMethod]
	public void When_A_Type_Cannot_Be_Read_Then_Only_That_Type_Is_Skipped()
	{
		AssertLogged($"skipped {Namespace}.IBroken [AotSeedingBroken]");
		Assert.IsTrue(
			GetTargets().Any(m => m.Parameters.Count == 9 && m.Parameters.All(p => p.ParameterType.FullName == "System.Int16")),
			"IHealthy.Check isn't seeded.");
	}

	// The bodies of ContainedStore<T>.Broken and ContainedGeneric.BrokenCall<T> can't be read: only they are skipped, not the instance
	// ContainedStore<ContainedKey>, whose interfaces are seeded, nor ContainedCaller.First, which calls BrokenCall<ContainedKey2> before it names
	// IContainedStore<ContainedKey2> in the same body.
	[TestMethod]
	[DataRow("ContainedKey", DisplayName = "The interfaces of an instance with a method body that can't be read")]
	[DataRow("ContainedKey2", DisplayName = "The code that calls a generic method whose body can't be read")]
	public void When_A_Method_Body_Cannot_Be_Read_Then_Only_That_Method_Is_Skipped(string key)
	{
		Assert.IsTrue(
			GetTargets().Any(m => m.Parameters.Count == 9 && m.Parameters[0].ParameterType.FullName == $"{Namespace}.{key}"),
			$"IContainedStore<{key}>.Find isn't seeded.");
		AssertLogged("::Broken() [AotSeedingContainment]: ");
		AssertLogged("::BrokenCall<T>() [AotSeedingContainment]: ");
	}

	// The scan of ContainedStore<T>'s and ContainedGeneric's code only skips Broken and BrokenCall, not the other methods of their types. BrokenCall
	// is reported once, for its definition and its two instances.
	[TestMethod]
	public void When_A_Method_Body_Cannot_Be_Read_Then_The_Other_Methods_Of_Its_Type_Are_Scanned()
	{
		var skipped = _engine.Messages.Select(m => m.Message).Where(m => m.Contains(" skipped ") && m.Contains("[AotSeedingContainment]")).ToList();

		Assert.IsTrue(skipped.Count > 0 && skipped.All(m => m.Contains("::Broken")), string.Join(Environment.NewLine, skipped));
		Assert.AreEqual(1, skipped.Count(m => m.Contains("::BrokenCall<T>()")), string.Join(Environment.NewLine, skipped));
	}

	// The UNOWASM010 check can't read the methods of IBroken either.
	[TestMethod]
	public void When_A_Type_Cannot_Be_Read_Then_UNOWASM010_Says_It_Could_Not_Check_It()
	{
		AssertLogged($"couldn't check {Namespace}.IBroken [AotSeedingBroken] for UNOWASM010: ");
	}

	// The parameter type of IScoped.Take is scoped to a module that the assembly doesn't have, which the reader can't resolve.
	[TestMethod]
	public void When_A_Reference_Cannot_Be_Resolved_Then_It_Is_Reported()
	{
		AssertLogged($"skipped {Namespace}.Thing [AotSeedingModuleScope]: NotSupportedException: ");
	}

	// Holder's layout can't be told, as the type of its field doesn't resolve, and neither can Thing's, which doesn't resolve itself.
	[TestMethod]
	public void When_UNOWASM010_Cannot_Tell_About_A_Method_Then_It_Says_So()
	{
		AssertLogged($"couldn't check {Namespace}.HolderMethods::Describe(int, int, int, int, int, int, int, int, Holder) [AotSeedingHolder] for UNOWASM010: ");
		AssertLogged($"couldn't check {Namespace}.HolderMethods::DescribeThing(int, Thing) [AotSeedingHolder] for UNOWASM010: Can't resolve ");
	}

	// A and B hold each other, and so do ExplicitA, with explicit layout, and ExplicitB (invalid metadata, which C# rejects): a walk through their
	// layout never ends. Their seeds are left out, and UNOWASM010 names A and ExplicitA, which come first, once each, for the methods that take
	// either struct of their cycle.
	[TestMethod]
	[DataRow("A", "B", "TakeCycle", DisplayName = "Sequential layout")]
	[DataRow("ExplicitA", "ExplicitB", "TakeExplicitCycle", DisplayName = "Explicit layout")]
	public void When_A_Layout_Is_Cyclic_Then_The_Signature_Is_Left_Out_And_UNOWASM010_Names_It(string type, string other, string method)
	{
		string warning = _engine.Warnings.Single(w => w.Code == "UNOWASM010" && w.Message.Contains($" {Namespace}.{type}, or a struct")).Message;

		StringAssert.Contains(warning, $"such as {Namespace}.ILayouts::{method}");
		Assert.IsFalse(_engine.Warnings.Any(w => w.Code == "UNOWASM010" && w.Message.Contains($" {Namespace}.{other}, or a struct")));

		foreach (string name in new[] { type, other })
		{
			Assert.IsFalse(GetTargets().SelectMany(m => m.Parameters).Any(p => p.ParameterType.FullName == $"{Namespace}.{name}"), $"{name} is seeded.");
		}
	}

	// LayoutMethods.Describe isn't seeded, and the UNOWASM010 check of its parameters can't tell the layout of C, which holds D, which holds C:
	// UNOWASM010 names C, with Describe.
	[TestMethod]
	public void When_A_Method_That_Only_UNOWASM010_Checks_Takes_A_Struct_Whose_Layout_Is_Cyclic_Then_UNOWASM010_Names_It()
	{
		string warning = _engine.Warnings.Single(w => w.Code == "UNOWASM010" && w.Message.Contains($" {Namespace}.C, or a struct")).Message;

		StringAssert.Contains(warning, $"such as {Namespace}.LayoutMethods::Describe [AotSeedingLayouts]");
		Assert.IsFalse(_engine.Messages.Any(m => m.Message.Contains("LayoutMethods::Describe(")), "UNOWASM010 couldn't check Describe.");
	}

	// Wide and CheckedWide have 16 type arguments, which get_wrapper_shared_type_full asserts against: ILayouts.TakeWide isn't seeded, and the
	// UNOWASM010 check of LayoutMethods.DescribeWide reaches CheckedWide through a field. UNOWASM010 names each struct once, with why.
	[TestMethod]
	[DataRow("Wide", "ILayouts::TakeWide [AotSeedingLayouts]", DisplayName = "A seed")]
	[DataRow("CheckedWide", "LayoutMethods::DescribeWide [AotSeedingLayouts]", DisplayName = "A method that only UNOWASM010 checks, through a field")]
	public void When_A_Generic_Struct_Has_16_Type_Arguments_Then_UNOWASM010_Names_It(string type, string method)
	{
		string name = $"{Namespace}.{type}<{string.Join(", ", Enumerable.Range(1, 16).Select(i => "T" + i))}>";
		string warning = _engine.Warnings.Single(w => w.Code == "UNOWASM010" && w.Message.Contains($" {name}, or a struct")).Message;

		StringAssert.Contains(warning, $"such as {Namespace}.{method}, or calls another method of its class through an interface: {name} has more than 15 ");
		StringAssert.Contains(warning, "type arguments, for which the runtime can't make the code that enters the interpreter, and neither can the crash ");
		StringAssert.Contains(warning, "Pass the struct with 'in', or give it at most 15 type arguments. To remove the warning, add UNOWASM010 to NoWarn.");
		Assert.IsFalse(
			GetTargets().SelectMany(m => m.Parameters).Any(p => p.ParameterType.Name.StartsWith("Wide`", StringComparison.Ordinal)),
			"Wide is seeded.");
		Assert.IsFalse(_engine.Messages.Any(m => m.Message.Contains("::DescribeWide(")), "UNOWASM010 couldn't check DescribeWide.");
	}

	// .NET 11's [ExtendedLayout] structs keep their own type, as explicit ones do: get_wrapper_shared_vtype only maps auto and sequential layouts.
	[TestMethod]
	public void When_A_Value_Type_Has_An_Extended_Layout_Then_It_Keeps_Its_Own_Type()
	{
		var origins = File.ReadAllLines(_files.ExpectedWrappers)
			.Select(l => l.Split('\t'))
			.Where(p => p[0] == "gsharedvt_in_sig_void_this_cl19_AotSeedingFailures_dUnion_")
			.Select(p => p[1])
			.ToList();

		CollectionAssert.Contains(origins, $"{Namespace}.ILayouts::TakeUnion [AotSeedingLayouts]");
		StringAssert.Contains(_engine.Warnings.Single(w => w.Code == "UNOWASM010" && w.Message.Contains("::DescribeUnion(")).Message, "parameter 9 ('value')");
	}

	[TestMethod]
	public void When_An_Input_Cannot_Be_Read_Then_Only_It_Is_Skipped()
	{
		AssertLogged($"skipped {_unreadable}: ");
	}

	// Trimmed assemblies have no parameter names. Above 8 parameters, the crash comes when the code that calls the method starts running, and for
	// a virtual instance method, which IMT slots can hold, when compiled code calls another method of the class through an interface.
	[TestMethod]
	[DataRow("Take(int, int, int, int, int, int, int, int, Box)", 9, "when it starts running, even if it doesn't make the call:",
		DisplayName = "9 parameters, static")]
	[DataRow("TakeVirtual(int, int, int, int, int, int, int, int, Box)", 9,
		"when it starts running, even if it doesn't make the call, and so can compiled code that calls another method of its class through an "
			+ "interface:",
		DisplayName = "9 parameters, virtual")]
	[DataRow("TakeFew(int, Box)", 2, "when it makes the call:", DisplayName = "2 parameters")]
	public void When_A_Parameter_That_The_Runtime_Keeps_Has_No_Name_Then_UNOWASM010_Gives_Its_Position(string method, int position, string when)
	{
		string warning = _engine.Warnings.Single(w => w.Code == "UNOWASM010" && w.Message.Contains($"::{method} [AotSeedingUnnamed]")).Message;

		StringAssert.Contains(warning, $"[Uno] Compiled code that calls {Namespace}.Methods::{method} [AotSeedingUnnamed] can crash the app {when}");
		StringAssert.Contains(warning, $"its parameter {position}, of type {Namespace}.Box, has an [Optional] attribute.");
		StringAssert.Contains(warning, "Unlike other structs, the runtime passes a struct with an explicit layout or many fields as it is between compiled");
		StringAssert.Contains(warning, "Remove the default value or the attribute, or pass the parameter with 'in'.");
		StringAssert.Contains(warning, "To remove the warning, add UNOWASM010 to NoWarn. See https://docs.example");
		Assert.AreEqual(when.Contains("interface"), warning.Contains("through an interface"), warning);
	}

	// Compiling such a method only moves the crash to the interpreted code that calls it (measured, see FR-8 in the spec), and the AOT compiler can
	// fail on the compiled calls to the methods of a struct that holds a struct over itself: neither warning sends to the profile.
	[TestMethod]
	public void When_UNOWASM010_Warns_Then_It_Does_Not_Advise_Recording_The_Profile()
	{
		var warnings = _engine.Warnings.Where(w => w.Code == "UNOWASM010").Select(w => w.Message).ToList();
		string unenterable = warnings.Single(w => w.Contains("::Take(int, int, int, int, int, int, int, int, Box) [AotSeedingUnnamed]"));
		string cyclic = warnings.Single(w => w.Contains($" {Namespace}.A, or a struct"));

		StringAssert.Contains(unenterable, "Listing the method in the AOT profile only moves the crash to the interpreted code that calls it.");
		StringAssert.Contains(unenterable, "If you can't change the method, report the warning to the library's authors.");
		StringAssert.Contains(cyclic, "Recording such calls in the AOT profile doesn't help: the AOT compiler can fail on them, which fails the build.");
		Assert.IsFalse(warnings.Any(w => w.Contains("while you record the AOT profile") || w.Contains("Unless the AOT profile")), string.Join("\n", warnings));
		Assert.IsTrue(warnings.All(w => w.Contains("To remove the warning, add UNOWASM010 to NoWarn.")), string.Join("\n", warnings));
	}

	// Go's instances and the tuples they make grow without end, which one warning reports.
	[TestMethod]
	public void When_Recursive_Generic_Code_Makes_Ever_Larger_Instances_Then_UNOWASM011_Names_It_Once()
	{
		string warning = _engine.Warnings.Single(w => w.Code == "UNOWASM011").Message;

		string limit = $"the generic instances that {Namespace}.Recursion::Go<A, B>() [AotSeedingRecursion] uses whose generic arguments hold more than "
			+ "64 types";
		StringAssert.Contains(warning, "stopped following " + limit + ".");
		StringAssert.Contains(warning, "Please report this warning at https://issues.example. To remove the warning, add UNOWASM011 to NoWarn.");
	}

	// The instances of FanOut.Go stay small, but grow in number without end.
	[TestMethod]
	public void When_Recursive_Generic_Code_Makes_Ever_More_Instances_Then_The_Instances_Of_Each_Definition_Are_Bounded()
	{
		var collector = new SignatureCollector { MaxInstancesPerDefinition = 100 };
		Collect(collector, _fanOut);

		CollectionAssert.Contains(collector.Limits.ToList(), $"the generic instances of {Namespace}.FanOut::Go<A, B>() [AotSeedingFanOut] past the first 100");
	}

	[TestMethod]
	public void When_Recursive_Generic_Code_Makes_Ever_More_Instances_Then_All_The_Instances_Are_Bounded()
	{
		var collector = new SignatureCollector { MaxFollowedInstances = 100 };
		Collect(collector, _fanOut);

		string limit = collector.Limits.Single();

		StringAssert.StartsWith(limit, "the generic instances past the first 100, ");
		StringAssert.EndsWith(limit, $" of which are of {Namespace}.FanOut::Go<A, B>() [AotSeedingFanOut]");
	}

	// Collects the signatures of one assembly, resolving the others in the runtime's folder.
	private static void Collect(SignatureCollector collector, string path)
	{
		using var resolver = new DefaultAssemblyResolver();
		resolver.AddSearchDirectory(Path.GetDirectoryName(CoreLibrary)!);
		using var module = ModuleDefinition.ReadModule(path, new ReaderParameters { AssemblyResolver = resolver });
		collector.AddModule(module);
	}

	// TypedReference can't be imported as a parameter type (SeedSignature.From never lets it through): the writer leaves that seed out.
	[TestMethod]
	public void When_A_Seed_Cannot_Be_Written_Then_The_Others_Are()
	{
		using var resolver = new DefaultAssemblyResolver();
		resolver.AddSearchDirectory(Path.GetDirectoryName(CoreLibrary)!);
		using var coreLibrary = AssemblyDefinition.ReadAssembly(CoreLibrary, new ReaderParameters { AssemblyResolver = resolver });
		var module = coreLibrary.MainModule;
		var origin = new SeedOrigin(module.GetType("System.Object").Methods.First(m => m.Name == "GetType"), null);
		var written = new SeedSignature(false, module.TypeSystem.Void, [module.TypeSystem.Int32], "static Void(Int32)", origin);
		var skipped = new SeedSignature(false, module.TypeSystem.Void, [module.GetType("System.TypedReference")], "static Void(TypedReference)", origin);

		var result = SeedAssemblyWriter.Write(coreLibrary, resolver, [written, skipped], SeedAssemblyName, SeedAssemblyWriter.SeedsPerMethod);

		CollectionAssert.AreEqual(new[] { written }, result.Seeds.ToList());
		Assert.AreEqual("object::GetType [System.Private.CoreLib]: NotSupportedException: Element type TypedByReference.", result.Skipped.Single());
	}

	// The files of an earlier build are deleted, so that the build doesn't use them.
	[TestMethod]
	public void When_The_Seeds_Cannot_Be_Generated_Then_UNOWASM008_Is_Logged_And_The_Build_Continues()
	{
		var files = TestSeedFiles.In(Path.Combine(_directory, "no-core-library"));
		Directory.CreateDirectory(Path.GetDirectoryName(files.Assembly)!);
		File.WriteAllText(files.Assembly, "");

		var engine = new RecordingBuildEngine();
		var task = CreateTask(engine, files, typeof(Given_AOTWrapperSeedingFailures).Assembly.Location);

		Assert.IsTrue(task.Execute());
		Assert.AreEqual("UNOWASM008", engine.Warnings.Single().Code);
		Assert.IsFalse(task.Generated);
		Assert.IsFalse(File.Exists(files.Assembly));
	}

	[TestMethod]
	public void When_Canceled_Then_It_Stops_Without_A_Warning_Or_A_File()
	{
		var engine = new RecordingBuildEngine();
		string directory = Path.Combine(_directory, "canceled");
		var task = CreateTask(engine, TestSeedFiles.In(directory), CoreLibrary);
		task.Cancel();

		Assert.IsFalse(task.Execute());
		Assert.AreEqual(0, engine.Warnings.Count + engine.Errors.Count);
		Assert.IsFalse(task.Generated);
		Assert.IsFalse(Directory.Exists(directory), "A file was written.");
	}

	private static void AssertLogged(string text)
		=> Assert.IsTrue(_engine.Messages.Any(m => m.Message.Contains(text)), $"No message has \"{text}\".");

	private static MethodDefinition[] GetTargets()
	{
		using var assembly = AssemblyDefinition.ReadAssembly(_files.Assembly, new ReaderParameters { InMemory = true });
		return assembly.MainModule.GetType(SeedAssemblyName + "." + SeedAssemblyWriter.SeedsTypeName).Methods
			.Where(m => m.Name.StartsWith("M", StringComparison.Ordinal))
			.ToArray();
	}

	private static GenerateAOTWrapperSeedsTask CreateTask(
		RecordingBuildEngine engine, AotWrapperSeedGenerator.SeedFiles files, params string[] assemblies) => new()
	{
		BuildEngine = engine,
		Assemblies = assemblies.Select(a => new TaskItem(a)).ToArray(),
		SeedAssembly = files.Assembly,
		SeedProfile = files.Profile,
		ExpectedWrappers = files.ExpectedWrappers,
		DocumentationUrl = "https://docs.example",
		IssuesUrl = "https://issues.example",
	};

	// IService.Take (Holder x 9), HolderMethods.Describe (int x 8, [Optional] Holder), where Holder has a field of AotSeedingMissing's Thing, an
	// assembly that isn't an input, and HolderMethods.DescribeThing (int, [Optional] Thing).
	private static string WriteHolder()
	{
		string missing = WriteAssembly("AotSeedingMissing", module =>
		{
			module.Types.Add(new TypeDefinition(Namespace, "Thing", TypeAttributes.Public | TypeAttributes.Sealed, module.ImportReference(typeof(ValueType))));
		});

		return WriteAssembly("AotSeedingHolder", module =>
		{
			var thing = new TypeReference(Namespace, "Thing", module, ReadAssemblyName(missing), true);
			var holderType = new TypeDefinition(Namespace, "Holder", TypeAttributes.Public | TypeAttributes.Sealed, module.ImportReference(typeof(ValueType)));
			holderType.Fields.Add(new FieldDefinition("Value", FieldAttributes.Public, thing));
			module.Types.Add(holderType);

			AddMethod(AddInterface(module, "IService"), "Take", holderType);

			var methods = new TypeDefinition(Namespace, "HolderMethods", TypeAttributes.Public | TypeAttributes.Abstract, module.TypeSystem.Object);
			var describe = new MethodDefinition("Describe", MethodAttributes.Public | MethodAttributes.Static, module.TypeSystem.Void);

			for (int i = 0; i < 8; i++)
			{
				describe.Parameters.Add(new ParameterDefinition("p" + i, ParameterAttributes.None, module.TypeSystem.Int32));
			}

			describe.Parameters.Add(new ParameterDefinition("value", ParameterAttributes.Optional, holderType));
			describe.Body.GetILProcessor().Emit(OpCodes.Ret);
			methods.Methods.Add(describe);

			var describeThing = new MethodDefinition("DescribeThing", MethodAttributes.Public | MethodAttributes.Static, module.TypeSystem.Void);
			describeThing.Parameters.Add(new ParameterDefinition("count", ParameterAttributes.None, module.TypeSystem.Int32));
			describeThing.Parameters.Add(new ParameterDefinition("value", ParameterAttributes.Optional, thing));
			describeThing.Body.GetILProcessor().Emit(OpCodes.Ret);
			methods.Methods.Add(describeThing);
			module.Types.Add(methods);
		});
	}

	// IBroken.Run, whose signature can't be read, and IHealthy.Check (short x 9).
	private static string WriteBroken()
	{
		string path = WriteAssembly("AotSeedingBroken", module =>
		{
			// Referencing the core library first makes the module's type system use it.
			module.ImportReference(typeof(object));
			AddMethod(AddInterface(module, "IBroken"), "Run", module.TypeSystem.Int32);
			AddMethod(AddInterface(module, "IHealthy"), "Check", module.TypeSystem.Int16);
		});

		CorruptReturnType(path, "Run");
		return path;
	}

	// IContainedStore<T>.Find (T, int x 8), ContainedStore<T> : IContainedStore<T> with a virtual Broken() before Find, ContainedUser.Use, which
	// names ContainedStore<ContainedKey>, ContainedGeneric.BrokenCall<T>(), and ContainedCaller, whose First calls BrokenCall<ContainedKey2>() and
	// then names IContainedStore<ContainedKey2>, and whose Second calls BrokenCall<ContainedKey>(). The bodies of Broken and BrokenCall can't be
	// read.
	private static string WriteContainment()
	{
		string path = WriteAssembly("AotSeedingContainment", module =>
		{
			module.ImportReference(typeof(object));
			var typeSystem = module.TypeSystem;
			var key = AddKey(module, "ContainedKey");
			var key2 = AddKey(module, "ContainedKey2");

			var store = AddInterface(module, "IContainedStore`1");
			var storeParameter = new GenericParameter("T", store);
			store.GenericParameters.Add(storeParameter);
			store.Methods.Add(CreateFind(module, storeParameter, MethodAttributes.Abstract | MethodAttributes.NewSlot));

			var implementation = new TypeDefinition(Namespace, "ContainedStore`1", TypeAttributes.Public | TypeAttributes.Sealed, typeSystem.Object);
			var implementationParameter = new GenericParameter("T", implementation);
			implementation.GenericParameters.Add(implementationParameter);
			implementation.Interfaces.Add(new InterfaceImplementation(Instantiate(store, implementationParameter)));
			var virtualMethod = MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig | MethodAttributes.NewSlot;
			var broken = new MethodDefinition("Broken", virtualMethod, typeSystem.Void);
			EmitTryCatch(module, broken);
			implementation.Methods.Add(broken);
			var find = CreateFind(module, implementationParameter, MethodAttributes.Final | MethodAttributes.NewSlot);
			find.Body.GetILProcessor().Emit(OpCodes.Ret);
			implementation.Methods.Add(find);
			module.Types.Add(implementation);

			var staticClass = TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed;
			var user = new TypeDefinition(Namespace, "ContainedUser", staticClass, typeSystem.Object);
			user.Methods.Add(CreateTokenMethod(module, "Use", Instantiate(implementation, key)));
			module.Types.Add(user);

			var generic = new TypeDefinition(Namespace, "ContainedGeneric", staticClass, typeSystem.Object);
			var brokenCall = new MethodDefinition("BrokenCall", MethodAttributes.Public | MethodAttributes.Static, typeSystem.Void);
			brokenCall.GenericParameters.Add(new GenericParameter("T", brokenCall));
			EmitTryCatch(module, brokenCall);
			generic.Methods.Add(brokenCall);
			module.Types.Add(generic);

			var caller = new TypeDefinition(Namespace, "ContainedCaller", staticClass, typeSystem.Object);
			var first = new MethodDefinition("First", MethodAttributes.Public | MethodAttributes.Static, typeSystem.Void);
			var call = new GenericInstanceMethod(brokenCall);
			call.GenericArguments.Add(key2);
			var il = first.Body.GetILProcessor();
			il.Emit(OpCodes.Call, call);
			il.Emit(OpCodes.Ldtoken, Instantiate(store, key2));
			il.Emit(OpCodes.Pop);
			il.Emit(OpCodes.Ret);
			caller.Methods.Add(first);
			var second = new MethodDefinition("Second", MethodAttributes.Public | MethodAttributes.Static, typeSystem.Void);
			var secondCall = new GenericInstanceMethod(brokenCall);
			secondCall.GenericArguments.Add(key);
			second.Body.GetILProcessor().Emit(OpCodes.Call, secondCall);
			second.Body.GetILProcessor().Emit(OpCodes.Ret);
			caller.Methods.Add(second);
			module.Types.Add(caller);
		});

		CorruptCatchType(path, "Broken");
		CorruptCatchType(path, "BrokenCall");
		return path;
	}

	// A struct with explicit layout, so that the wrapper names keep it.
	private static TypeDefinition AddKey(ModuleDefinition module, string name)
	{
		var attributes = TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.ExplicitLayout;
		var key = new TypeDefinition(Namespace, name, attributes, module.ImportReference(typeof(ValueType)));
		key.Fields.Add(new FieldDefinition("Value", FieldAttributes.Public, module.TypeSystem.Int32) { Offset = 0 });
		module.Types.Add(key);
		return key;
	}

	private static GenericInstanceType Instantiate(TypeReference type, TypeReference argument)
	{
		var instance = new GenericInstanceType(type);
		instance.GenericArguments.Add(argument);
		return instance;
	}

	// Find (T, int x 8)
	private static MethodDefinition CreateFind(ModuleDefinition module, TypeReference first, MethodAttributes attributes)
	{
		var virtualMethod = MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig;
		var find = new MethodDefinition("Find", virtualMethod | attributes, module.TypeSystem.Void);
		find.Parameters.Add(new ParameterDefinition("a", ParameterAttributes.None, first));

		for (int i = 0; i < 8; i++)
		{
			find.Parameters.Add(new ParameterDefinition("p" + i, ParameterAttributes.None, module.TypeSystem.Int32));
		}

		return find;
	}

	// A static method that names the type with ldtoken.
	private static MethodDefinition CreateTokenMethod(ModuleDefinition module, string name, TypeReference type)
	{
		var method = new MethodDefinition(name, MethodAttributes.Public | MethodAttributes.Static, module.TypeSystem.Void);
		var il = method.Body.GetILProcessor();
		il.Emit(OpCodes.Ldtoken, type);
		il.Emit(OpCodes.Pop);
		il.Emit(OpCodes.Ret);
		return method;
	}

	// An empty try with a catch clause, whose catch type CorruptCatchType replaces.
	private static void EmitTryCatch(ModuleDefinition module, MethodDefinition method)
	{
		var il = method.Body.GetILProcessor();
		var end = il.Create(OpCodes.Ret);
		var tryStart = il.Create(OpCodes.Nop);
		var handlerStart = il.Create(OpCodes.Pop);
		il.Append(tryStart);
		il.Emit(OpCodes.Leave, end);
		il.Append(handlerStart);
		il.Emit(OpCodes.Leave, end);
		il.Append(end);

		method.Body.ExceptionHandlers.Add(new ExceptionHandler(ExceptionHandlerType.Catch)
		{
			TryStart = tryStart,
			TryEnd = handlerStart,
			HandlerStart = handlerStart,
			HandlerEnd = end,
			CatchType = module.ImportReference(typeof(Exception)),
		});
	}

	// IScoped.Take (Thing x 9), where Thing is scoped to a module the assembly doesn't have.
	private static string WriteModuleScope() => WriteAssembly("AotSeedingModuleScope", module =>
	{
		// Referencing the core library first makes the module's type system use it.
		module.ImportReference(typeof(object));
		var missing = new ModuleReference("Missing.netmodule");
		module.ModuleReferences.Add(missing);
		AddMethod(AddInterface(module, "IScoped"), "Take", new TypeReference(Namespace, "Thing", module, missing, true));
	});

	// Methods.Take (int x 8, [Optional] Box), TakeFew (int, [Optional] Box) and the virtual instance method TakeVirtual (int x 8, [Optional]
	// Box), with explicit layout for Box, and no parameter names.
	private static string WriteUnnamed() => WriteAssembly("AotSeedingUnnamed", module =>
	{
		var attributes = TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.ExplicitLayout;
		var box = new TypeDefinition(Namespace, "Box", attributes, module.ImportReference(typeof(ValueType)));
		box.Fields.Add(new FieldDefinition("Value", FieldAttributes.Public, module.ImportReference(typeof(int))) { Offset = 0 });
		module.Types.Add(box);

		var methods = new TypeDefinition(Namespace, "Methods", TypeAttributes.Public | TypeAttributes.Abstract, module.ImportReference(typeof(object)));
		var take = new MethodDefinition("Take", MethodAttributes.Public | MethodAttributes.Static, module.ImportReference(typeof(void)));

		for (int i = 0; i < 8; i++)
		{
			take.Parameters.Add(new ParameterDefinition(module.ImportReference(typeof(int))));
		}

		take.Parameters.Add(new ParameterDefinition(box) { Attributes = ParameterAttributes.Optional });
		take.Body.GetILProcessor().Emit(OpCodes.Ret);
		methods.Methods.Add(take);

		var takeFew = new MethodDefinition("TakeFew", MethodAttributes.Public | MethodAttributes.Static, module.ImportReference(typeof(void)));
		takeFew.Parameters.Add(new ParameterDefinition(module.ImportReference(typeof(int))));
		takeFew.Parameters.Add(new ParameterDefinition(box) { Attributes = ParameterAttributes.Optional });
		takeFew.Body.GetILProcessor().Emit(OpCodes.Ret);
		methods.Methods.Add(takeFew);

		var virtualMethod = MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.NewSlot | MethodAttributes.HideBySig;
		var takeVirtual = new MethodDefinition("TakeVirtual", virtualMethod, module.ImportReference(typeof(void)));

		for (int i = 0; i < 8; i++)
		{
			takeVirtual.Parameters.Add(new ParameterDefinition(module.ImportReference(typeof(int))));
		}

		takeVirtual.Parameters.Add(new ParameterDefinition(box) { Attributes = ParameterAttributes.Optional });
		takeVirtual.Body.GetILProcessor().Emit(OpCodes.Ret);
		methods.Methods.Add(takeVirtual);
		module.Types.Add(methods);
	});

	// ILayouts.TakeCycle (A) and TakeCycleB (B), where A holds a B that holds an A, ILayouts.TakeExplicitCycle (ExplicitA) and TakeExplicitCycleB
	// (ExplicitB), the same with explicit layout, ILayouts.TakeUnion (Union), where Union has an extended layout, and LayoutMethods.Describe (int x
	// 8, [Optional] D), where D holds a C that holds a D, and DescribeUnion (int x 8, [Optional] Union).
	private static string WriteLayouts() => WriteAssembly("AotSeedingLayouts", module =>
	{
		var valueType = module.ImportReference(typeof(ValueType));
		var a = new TypeDefinition(Namespace, "A", TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.SequentialLayout, valueType);
		var b = new TypeDefinition(Namespace, "B", TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.SequentialLayout, valueType);
		a.Fields.Add(new FieldDefinition("b", FieldAttributes.Public, b));
		b.Fields.Add(new FieldDefinition("a", FieldAttributes.Public, a));
		module.Types.Add(a);
		module.Types.Add(b);

		// The same, for a method that only the UNOWASM010 check reads.
		var c = new TypeDefinition(Namespace, "C", TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.SequentialLayout, valueType);
		var d = new TypeDefinition(Namespace, "D", TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.SequentialLayout, valueType);
		c.Fields.Add(new FieldDefinition("d", FieldAttributes.Public, d));
		d.Fields.Add(new FieldDefinition("c", FieldAttributes.Public, c));
		module.Types.Add(c);
		module.Types.Add(d);

		var extendedLayout = TypeAttributes.SequentialLayout | TypeAttributes.ExplicitLayout;
		var explicitA = new TypeDefinition(Namespace, "ExplicitA", TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.ExplicitLayout, valueType);
		var explicitB = new TypeDefinition(Namespace, "ExplicitB", TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.SequentialLayout, valueType);
		explicitA.Fields.Add(new FieldDefinition("b", FieldAttributes.Public, explicitB) { Offset = 0 });
		explicitB.Fields.Add(new FieldDefinition("a", FieldAttributes.Public, explicitA));
		module.Types.Add(explicitA);
		module.Types.Add(explicitB);

		var union = new TypeDefinition(Namespace, "Union", TypeAttributes.Public | TypeAttributes.Sealed | extendedLayout, valueType);
		union.Fields.Add(new FieldDefinition("First", FieldAttributes.Public, module.ImportReference(typeof(int))));
		union.Fields.Add(new FieldDefinition("Second", FieldAttributes.Public, module.ImportReference(typeof(int))));
		module.Types.Add(union);

		// Generic structs with 16 type arguments, closed over int: Wide for a seed, and CheckedWide, in a field of CheckedWideHolder, for a method
		// that only the UNOWASM010 check reads.
		var wide = AddWideStruct(module, "Wide`16", 16);
		var checkedWide = AddWideStruct(module, "CheckedWide`16", 16);
		var checkedWideHolder = new TypeDefinition(
			Namespace, "CheckedWideHolder", TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.SequentialLayout, valueType);
		checkedWideHolder.Fields.Add(new FieldDefinition("Wide", FieldAttributes.Public, checkedWide));
		module.Types.Add(checkedWideHolder);

		var layouts = AddInterface(module, "ILayouts");
		var attributes = MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Abstract | MethodAttributes.HideBySig | MethodAttributes.NewSlot;
		var takeCycle = new MethodDefinition("TakeCycle", attributes, module.ImportReference(typeof(void)));
		takeCycle.Parameters.Add(new ParameterDefinition("value", ParameterAttributes.None, a));
		layouts.Methods.Add(takeCycle);
		var takeCycleB = new MethodDefinition("TakeCycleB", attributes, module.ImportReference(typeof(void)));
		takeCycleB.Parameters.Add(new ParameterDefinition("value", ParameterAttributes.None, b));
		layouts.Methods.Add(takeCycleB);
		var takeExplicitCycle = new MethodDefinition("TakeExplicitCycle", attributes, module.ImportReference(typeof(void)));
		takeExplicitCycle.Parameters.Add(new ParameterDefinition("value", ParameterAttributes.None, explicitA));
		layouts.Methods.Add(takeExplicitCycle);
		var takeExplicitCycleB = new MethodDefinition("TakeExplicitCycleB", attributes, module.ImportReference(typeof(void)));
		takeExplicitCycleB.Parameters.Add(new ParameterDefinition("value", ParameterAttributes.None, explicitB));
		layouts.Methods.Add(takeExplicitCycleB);
		var takeUnion = new MethodDefinition("TakeUnion", attributes, module.ImportReference(typeof(void)));
		takeUnion.Parameters.Add(new ParameterDefinition("value", ParameterAttributes.None, union));
		layouts.Methods.Add(takeUnion);
		var takeWide = new MethodDefinition("TakeWide", attributes, module.ImportReference(typeof(void)));
		takeWide.Parameters.Add(new ParameterDefinition("value", ParameterAttributes.None, wide));
		layouts.Methods.Add(takeWide);

		var methods = new TypeDefinition(Namespace, "LayoutMethods", TypeAttributes.Public | TypeAttributes.Abstract, module.ImportReference(typeof(object)));
		var describe = new MethodDefinition("Describe", MethodAttributes.Public | MethodAttributes.Static, module.ImportReference(typeof(void)));

		for (int i = 0; i < 8; i++)
		{
			describe.Parameters.Add(new ParameterDefinition("p" + i, ParameterAttributes.None, module.ImportReference(typeof(int))));
		}

		describe.Parameters.Add(new ParameterDefinition("value", ParameterAttributes.Optional, d));
		describe.Body.GetILProcessor().Emit(OpCodes.Ret);
		methods.Methods.Add(describe);

		var describeUnion = new MethodDefinition("DescribeUnion", MethodAttributes.Public | MethodAttributes.Static, module.ImportReference(typeof(void)));

		for (int i = 0; i < 8; i++)
		{
			describeUnion.Parameters.Add(new ParameterDefinition("p" + i, ParameterAttributes.None, module.ImportReference(typeof(int))));
		}

		describeUnion.Parameters.Add(new ParameterDefinition("value", ParameterAttributes.Optional, union));
		describeUnion.Body.GetILProcessor().Emit(OpCodes.Ret);
		methods.Methods.Add(describeUnion);

		var describeWide = new MethodDefinition("DescribeWide", MethodAttributes.Public | MethodAttributes.Static, module.ImportReference(typeof(void)));
		describeWide.Parameters.Add(new ParameterDefinition("value", ParameterAttributes.Optional, checkedWideHolder));
		describeWide.Body.GetILProcessor().Emit(OpCodes.Ret);
		methods.Methods.Add(describeWide);
		module.Types.Add(methods);
	});

	// A sequential generic struct with the given number of type parameters, with fields of the first and the last, closed over int.
	private static GenericInstanceType AddWideStruct(ModuleDefinition module, string name, int arity)
	{
		var attributes = TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.SequentialLayout;
		var definition = new TypeDefinition(Namespace, name, attributes, module.ImportReference(typeof(ValueType)));

		for (int i = 1; i <= arity; i++)
		{
			definition.GenericParameters.Add(new GenericParameter("T" + i, definition));
		}

		definition.Fields.Add(new FieldDefinition("First", FieldAttributes.Public, definition.GenericParameters[0]));
		definition.Fields.Add(new FieldDefinition("Last", FieldAttributes.Public, definition.GenericParameters[arity - 1]));
		module.Types.Add(definition);

		// The input CoreLib's int: the module's TypeSystem.Int32 is mscorlib's, which isn't an input.
		var instance = new GenericInstanceType(definition);
		var int32 = module.ImportReference(typeof(int));

		for (int i = 0; i < arity; i++)
		{
			instance.GenericArguments.Add(int32);
		}

		return instance;
	}

	// Recursion.Go<A, B> calls Go<(A, B), A>, Go<B, (B, A)> and Go<A, (A, A)>, and Use calls Go<int, long>. Its int and long are mscorlib's, which
	// isn't an input, so that only the instances of Go are followed.
	private static string WriteRecursion()
		=> WriteGo("AotSeedingRecursion", "Recursion", (pair, a, b, _) => [(pair(a, b), a), (b, pair(b, a)), (a, pair(a, a))]);

	// FanOut.Go<A, B> calls Go<(A, X), B> and Go<A, (B, X)> for X in int, long and short: its instances hold at most 34 types (nesting stops at 8),
	// but there are millions of them. Use calls Go<int, long>.
	private static string WriteFanOut() => WriteGo("AotSeedingFanOut", "FanOut", (pair, a, b, types)
		=> types.SelectMany(x => new[] { (pair(a, x), b), (a, pair(b, x)) }).ToArray());

	// The pairs of arguments that Go<A, B> calls itself over, given a function that makes a tuple of two types, Go's A and B, and int, long and short.
	private delegate (TypeReference A, TypeReference B)[] GoCalls(
		Func<TypeReference, TypeReference, TypeReference> pair, TypeReference a, TypeReference b, TypeReference[] types);

	// A class with a method Go<A, B> that calls the instances of Go over the given pairs of arguments, and a method Use that calls Go<int, long>.
	private static string WriteGo(string assemblyName, string typeName, GoCalls calls)
		=> WriteAssembly(assemblyName, module =>
		{
			var attributes = TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed;
			var type = new TypeDefinition(Namespace, typeName, attributes, module.TypeSystem.Object);
			module.Types.Add(type);

			var go = new MethodDefinition("Go", MethodAttributes.Public | MethodAttributes.Static, module.TypeSystem.Int32);
			var a = new GenericParameter("A", go);
			var b = new GenericParameter("B", go);
			go.GenericParameters.Add(a);
			go.GenericParameters.Add(b);
			type.Methods.Add(go);

			var tuple = module.ImportReference(typeof(ValueTuple<,>));

			TypeReference Pair(TypeReference first, TypeReference second)
			{
				var pair = new GenericInstanceType(tuple);
				pair.GenericArguments.Add(first);
				pair.GenericArguments.Add(second);
				return pair;
			}

			MethodReference Go(TypeReference first, TypeReference second)
			{
				var instance = new GenericInstanceMethod(go);
				instance.GenericArguments.Add(first);
				instance.GenericArguments.Add(second);
				return instance;
			}

			var il = go.Body.GetILProcessor();
			var typeSystem = module.TypeSystem;

			foreach (var (first, second) in calls(Pair, a, b, [typeSystem.Int32, typeSystem.Int64, typeSystem.Int16]))
			{
				il.Emit(OpCodes.Call, Go(first, second));
				il.Emit(OpCodes.Pop);
			}

			il.Emit(OpCodes.Ldc_I4_0);
			il.Emit(OpCodes.Ret);

			var use = new MethodDefinition("Use", MethodAttributes.Public | MethodAttributes.Static, typeSystem.Int32);
			use.Body.GetILProcessor().Emit(OpCodes.Call, Go(typeSystem.Int32, typeSystem.Int64));
			use.Body.GetILProcessor().Emit(OpCodes.Ret);
			type.Methods.Add(use);
		});

	private static string WriteAssembly(string name, Action<ModuleDefinition> addTypes)
	{
		using var assembly = AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition(name, new Version(1, 0, 0, 0)), name, ModuleKind.Dll);
		addTypes(assembly.MainModule);

		string path = Path.Combine(_directory, name + ".dll");
		assembly.Write(path);
		return path;
	}

	private static AssemblyNameReference ReadAssemblyName(string path)
	{
		using var assembly = AssemblyDefinition.ReadAssembly(path);
		return AssemblyNameReference.Parse(assembly.FullName);
	}

	private static TypeDefinition AddInterface(ModuleDefinition module, string name)
	{
		var type = new TypeDefinition(Namespace, name, TypeAttributes.Public | TypeAttributes.Interface | TypeAttributes.Abstract);
		module.Types.Add(type);
		return type;
	}

	// An interface method with 9 parameters of the given type.
	private static void AddMethod(TypeDefinition type, string name, TypeReference parameterType)
	{
		var attributes = MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Abstract | MethodAttributes.HideBySig | MethodAttributes.NewSlot;
		var method = new MethodDefinition(name, attributes, type.Module.TypeSystem.Void);

		for (int i = 0; i < 9; i++)
		{
			method.Parameters.Add(new ParameterDefinition("p" + i, ParameterAttributes.None, parameterType));
		}

		type.Methods.Add(method);
	}

	// Replaces the catch type of the first exception clause of the method's body with a method token, which the reader can't read as a type.
	private static void CorruptCatchType(string path, string methodName)
	{
		byte[] bytes = File.ReadAllBytes(path);
		int offset;

		using (var reader = new PEReader(new MemoryStream(bytes)))
		{
			var metadata = System.Reflection.Metadata.PEReaderExtensions.GetMetadataReader(reader);
			var method = metadata.MethodDefinitions.Select(metadata.GetMethodDefinition).Single(m => metadata.GetString(m.Name) == methodName);
			int rva = method.RelativeVirtualAddress;
			var section = reader.PEHeaders.SectionHeaders.Single(s => rva >= s.VirtualAddress && rva < s.VirtualAddress + s.VirtualSize);
			int body = rva - section.VirtualAddress + section.PointerToRawData;

			// A fat header with more sections, then the code, then a small exception handling section, 4-byte aligned: its header (4 bytes), then
			// the clause's flags (2), try offset (2) and length (1), handler offset (2) and length (1), and catch type token (4).
			Assert.AreEqual(0x0B, bytes[body] & 0x0B, "Not a fat header with more sections.");
			int codeSize = BitConverter.ToInt32(bytes, body + 4);
			int section0 = (body + 12 + codeSize + 3) & ~3;
			Assert.AreEqual(0x01, bytes[section0] & 0x41, "Not a small exception handling section.");
			offset = section0 + 4 + 8;
			Assert.AreEqual(0x01, bytes[offset + 3], "The catch type isn't a type reference.");
		}

		BitConverter.GetBytes(0x06000001).CopyTo(bytes, offset);
		File.WriteAllBytes(path, bytes);
	}

	// Replaces the return type in the signature of the method with an element type that doesn't exist.
	private static void CorruptReturnType(string path, string methodName)
	{
		byte[] bytes = File.ReadAllBytes(path);
		int offset;

		using (var reader = new PEReader(new MemoryStream(bytes)))
		{
			var metadata = System.Reflection.Metadata.PEReaderExtensions.GetMetadataReader(reader);
			var method = metadata.MethodDefinitions.Select(metadata.GetMethodDefinition).Single(m => metadata.GetString(m.Name) == methodName);

			// The blob is its length, the calling convention, the parameter count and the return type.
			offset = reader.PEHeaders.MetadataStartOffset + metadata.GetHeapMetadataOffset(HeapIndex.Blob) + MetadataTokens.GetHeapOffset(method.Signature) + 3;
		}

		Assert.AreEqual((byte)System.Reflection.Metadata.SignatureTypeCode.Void, bytes[offset]);
		bytes[offset] = 0x3F;
		File.WriteAllBytes(path, bytes);
	}
}

// With explicit layout, the runtime doesn't map it to a Mono.ValueTuple, so a parameter of this type keeps its flags.
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Explicit)]
public struct AotSeedingExplicitBox
{
	[System.Runtime.InteropServices.FieldOffset(0)]
	public int Value;
}

public static class AotSeedingAttributedParameters
{
	public static void WithDefault(int a, int b, int c, int d, int e, int f, int g, int h, AotSeedingExplicitBox box = default)
	{
	}

	// Passed by reference: the runtime replaces the type.
	public static void WithIn(int a, int b, int c, int d, int e, int f, int g, int h, in AotSeedingExplicitBox box)
	{
	}

	// 8 parameters or fewer enter the interpreter through gsharedvt_in_sig wrappers, which have the same problem, as the test app's variants showed:
	// called from compiled code, a method of 8 parameters like this one stops the app with "function signature mismatch".
	public static void WithEightParameters(int a, int b, int c, int d, int e, int f, int g, AotSeedingExplicitBox box = default)
	{
	}

	public static void WithFewParameters(int a, AotSeedingExplicitBox box = default)
	{
	}

	// Without a default value, the parameter has no flags: the same method runs.
	public static void WithoutDefault(int a, int b, int c, int d, int e, int f, int g, AotSeedingExplicitBox box)
	{
	}

	public static void WithMarshalAs(int a, [MarshalAs(UnmanagedType.Struct)] AotSeedingExplicitBox box)
	{
	}

	public static void WithInAndOut(int a, [In, Out] AotSeedingExplicitBox box)
	{
	}

	// The flags of a return value don't matter: the wrapper caches only hash the return type's element type, and the test app's variants with
	// these methods run.
	[return: MarshalAs(UnmanagedType.Struct)]
	public static AotSeedingExplicitBox WithMarshaledReturn(int a) => default;

	[return: MarshalAs(UnmanagedType.Struct)]
	public static AotSeedingExplicitBox WithMarshaledReturnAndNineParameters(int a, int b, int c, int d, int e, int f, int g, int h, int i) => default;

	// The runtime maps AotSeedingKey to a Mono.ValueTuple.
	public static void WithMappedType(int a, int b, int c, int d, int e, int f, int g, int h, AotSeedingKey key = default)
	{
	}
}
