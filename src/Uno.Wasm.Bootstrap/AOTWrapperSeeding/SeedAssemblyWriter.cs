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
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Uno.Wasm.Bootstrap.AOTWrapperSeeding;

/// <summary>
/// Writes the seed assembly. For every seeded signature it has a target method of that signature (<c>M0</c>, <c>M1</c>, ...), which the AOT profile
/// doesn't list, so it isn't compiled. The seed methods (<c>S0</c>, <c>S1</c>, ...), which the profile lists, call the targets directly. A direct call
/// from compiled code into a method of the same assembly that the profile doesn't list may enter the interpreter, so the AOT compiler records the
/// callee's signature (can_enter_interp and mini_emit_method_call_full in calls.c) and emits its interpreter entry wrapper: gsharedvt_in_sig, or
/// interp_in above 8 parameters (compile_method in aot-compiler.c). Nothing ever calls the seed methods, and they never make their calls.
/// </summary>
internal static class SeedAssemblyWriter
{
	public const string SeedsTypeName = "Seeds";

	/// <summary>
	/// The seeds each seed method calls. When the AOT compiler can't compile a seed method (a type of one of its signatures fails to load, for
	/// example), only the wrappers of its own seeds are lost, which the check reports.
	/// </summary>
	public const int SeedsPerMethod = 16;

	/// <param name="Image">The assembly.</param>
	/// <param name="MethodNames">The names of the seed methods, which are all <c>static void()</c> methods of <c>&lt;assembly name&gt;.Seeds</c>.</param>
	/// <param name="Seeds">The seeds the seed methods call.</param>
	/// <param name="Skipped">The seeds that couldn't be written, with why.</param>
	public sealed record Result(byte[] Image, IReadOnlyList<string> MethodNames, IReadOnlyList<SeedSignature> Seeds, IReadOnlyList<string> Skipped);

	/// <summary>
	/// Writes the assembly. A seed that can't be written only skips itself: when writing them all fails, each is written alone, and those that fail
	/// are left out.
	/// </summary>
	public static Result Write(
		AssemblyDefinition coreLibrary, IAssemblyResolver resolver, IReadOnlyList<SeedSignature> seeds, string assemblyName, int seedsPerMethod)
	{
		try
		{
			return Build(coreLibrary, resolver, seeds, assemblyName, seedsPerMethod);
		}
		catch (Exception e) when (e is not OutOfMemoryException && seeds.Count > 1)
		{
			// Importing a type or writing the image, which writes some metadata only then, can fail.
			var skipped = new List<string>();
			var writable = seeds.Where(seed =>
			{
				try
				{
					Build(coreLibrary, resolver, [seed], assemblyName, seedsPerMethod);
					return true;
				}
				catch (Exception seedException) when (seedException is not OutOfMemoryException)
				{
					skipped.Add($"{seed.Origin}: {seedException.GetType().Name}: {seedException.Message}");
					return false;
				}
			}).ToList();

			if (skipped.Count == 0)
			{
				throw;
			}

			return Build(coreLibrary, resolver, writable, assemblyName, seedsPerMethod) with { Skipped = skipped };
		}
	}

	private static Result Build(
		AssemblyDefinition coreLibrary, IAssemblyResolver resolver, IReadOnlyList<SeedSignature> seeds, string assemblyName, int seedsPerMethod)
	{
		using var assembly = AssemblyDefinition.CreateAssembly(
			new AssemblyNameDefinition(assemblyName, new Version(1, 0, 0, 0)),
			assemblyName,
			new ModuleParameters { Kind = ModuleKind.Dll, AssemblyResolver = resolver, Runtime = TargetRuntime.Net_4_0 });

		var module = assembly.MainModule;

		// Referencing the core library first makes the module's type system use it, so that primitives are written as element types.
		module.ImportReference(coreLibrary.MainModule.GetType("System.Object"));
		var objectType = module.TypeSystem.Object;
		var voidType = module.TypeSystem.Void;

		var typeAttributes = TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.BeforeFieldInit;
		var type = new TypeDefinition(assemblyName, SeedsTypeName, typeAttributes, objectType);
		module.Types.Add(type);

		var methodNames = new List<string>();

		for (int start = 0; start < seeds.Count; start += seedsPerMethod)
		{
			string name = "S" + methodNames.Count;
			var method = new MethodDefinition(name, MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig, voidType);
			method.Body.InitLocals = true;
			var il = method.Body.GetILProcessor();
			var locals = new Dictionary<string, VariableDefinition>(StringComparer.Ordinal);

			// The calls are on the fall-through path of a branch that is always taken (brfalse on 0), so they never run. The AOT compiler converts
			// every block that a branch can reach (mono_method_to_ir only skips the blocks that mono_basic_block_split in mono-basic-block.c finds
			// unreachable), which records the signatures of the calls, then removes them as dead code: the seed methods compile to almost nothing.
			// After an unconditional branch, the calls would be unreachable, and skipped with their signatures.
			var end = il.Create(OpCodes.Ret);
			il.Emit(OpCodes.Ldc_I4_0);
			il.Emit(OpCodes.Brfalse, end);

			for (int i = start; i < Math.Min(start + seedsPerMethod, seeds.Count); i++)
			{
				var seed = seeds[i];
				var target = DefineTarget(module, "M" + i, seed);
				type.Methods.Add(target);

				if (seed.HasThis)
				{
					il.Emit(OpCodes.Ldnull);
				}

				foreach (var parameterType in target.Parameters.Select(p => p.ParameterType))
				{
					PushDefault(method, il, parameterType, locals);
				}

				il.Emit(OpCodes.Call, target);

				if (target.ReturnType.MetadataType != MetadataType.Void)
				{
					il.Emit(OpCodes.Pop);
				}
			}

			il.Append(end);
			type.Methods.Add(method);
			methodNames.Add(name);
		}

		// The module version ID is a hash of the image, PE timestamp included, which Cecil sets to the current time unless given.
		using var image = new MemoryStream();
		assembly.Write(image, new WriterParameters { DeterministicMvid = true, Timestamp = 0 });
		return new Result(image.ToArray(), methodNames, seeds.ToList(), []);
	}

	// A method of the seeded signature that only throws. It must not be inlined: the call is what makes the compiler emit the wrapper.
	private static MethodDefinition DefineTarget(ModuleDefinition module, string name, SeedSignature seed)
	{
		var attributes = MethodAttributes.Public | MethodAttributes.HideBySig | (seed.HasThis ? 0 : MethodAttributes.Static);
		var target = new MethodDefinition(name, attributes, ImportTargetType(module, seed.ReturnType))
		{
			ImplAttributes = MethodImplAttributes.NoInlining,
		};

		for (int i = 0; i < seed.ParameterTypes.Count; i++)
		{
			target.Parameters.Add(new ParameterDefinition("p" + i, ParameterAttributes.None, ImportTargetType(module, seed.ParameterTypes[i])));
		}

		var il = target.Body.GetILProcessor();
		il.Emit(OpCodes.Ldnull);
		il.Emit(OpCodes.Throw);
		return target;
	}

	// The runtime passes all references alike (SeedSignature merges them too), so the targets take and return them as object, byrefs included.
	// The seed assembly then references fewer types, for which the AOT compiler would emit wrappers the seeding doesn't need (the invoke wrappers
	// of delegate types, for example), and a reference type that can't be imported can't make it skip a seed.
	private static TypeReference ImportTargetType(ModuleDefinition module, TypeReference type)
	{
		type = CecilTypes.StripModifiers(type);

		return type switch
		{
			ByReferenceType byRef => new ByReferenceType(ImportTargetType(module, byRef.ElementType)),
			PointerType or FunctionPointerType => Import(module, type),
			_ => CecilTypes.IsValueType(type) ? Import(module, type) : module.TypeSystem.Object,
		};
	}

	// Imports the type through its definitions, so that the seed assembly references the assemblies that define the types rather than facades.
	private static TypeReference Import(ModuleDefinition module, TypeReference type)
	{
		type = CecilTypes.StripModifiers(type);

		switch (type)
		{
			case ByReferenceType byRef:
				return new ByReferenceType(Import(module, byRef.ElementType));
			case PointerType pointer:
				return new PointerType(Import(module, pointer.ElementType));
			case FunctionPointerType function:
				var imported = new FunctionPointerType
				{
					ReturnType = Import(module, function.ReturnType),
					CallingConvention = function.CallingConvention,
					HasThis = function.HasThis,
					ExplicitThis = function.ExplicitThis,
				};

				foreach (var parameter in function.Parameters)
				{
					imported.Parameters.Add(new ParameterDefinition(Import(module, parameter.ParameterType)));
				}

				return imported;
			case ArrayType array:
				return array.IsVector ? new ArrayType(Import(module, array.ElementType)) : new ArrayType(Import(module, array.ElementType), array.Rank);
			case GenericInstanceType instance:
				var importedInstance = new GenericInstanceType(module.ImportReference(CecilTypes.TryResolve(instance.ElementType) ?? instance.ElementType));

				foreach (var argument in instance.GenericArguments)
				{
					importedInstance.GenericArguments.Add(Import(module, argument));
				}

				return importedInstance;
			default:
				return PrimitiveTypes.Get(type) is MetadataType elementType
					? GetPrimitive(module.TypeSystem, elementType)
					: module.ImportReference(CecilTypes.TryResolve(type) ?? type);
		}
	}

	// The element type, which the seeds never have as a TypedReference (SeedSignature.IsSupported).
	private static TypeReference GetPrimitive(TypeSystem typeSystem, MetadataType elementType) => elementType switch
	{
		MetadataType.Void => typeSystem.Void,
		MetadataType.Boolean => typeSystem.Boolean,
		MetadataType.Char => typeSystem.Char,
		MetadataType.SByte => typeSystem.SByte,
		MetadataType.Byte => typeSystem.Byte,
		MetadataType.Int16 => typeSystem.Int16,
		MetadataType.UInt16 => typeSystem.UInt16,
		MetadataType.Int32 => typeSystem.Int32,
		MetadataType.UInt32 => typeSystem.UInt32,
		MetadataType.Int64 => typeSystem.Int64,
		MetadataType.UInt64 => typeSystem.UInt64,
		MetadataType.Single => typeSystem.Single,
		MetadataType.Double => typeSystem.Double,
		MetadataType.IntPtr => typeSystem.IntPtr,
		MetadataType.UIntPtr => typeSystem.UIntPtr,
		MetadataType.String => typeSystem.String,
		MetadataType.Object => typeSystem.Object,
		_ => throw new NotSupportedException($"Element type {elementType}."),
	};

	// Emits the instructions that push the default value of the type.
	private static void PushDefault(MethodDefinition method, ILProcessor il, TypeReference type, Dictionary<string, VariableDefinition> locals)
	{
		if (type is ByReferenceType byRef)
		{
			il.Emit(OpCodes.Ldloca, GetLocal(method, byRef.ElementType, locals));
			return;
		}

		if (type is PointerType or FunctionPointerType)
		{
			il.Emit(OpCodes.Ldc_I4_0);
			il.Emit(OpCodes.Conv_U);
			return;
		}

		if (!CecilTypes.IsValueType(type))
		{
			il.Emit(OpCodes.Ldnull);
			return;
		}

		switch (PrimitiveTypes.Get(CecilTypes.GetEnumUnderlyingType(type) ?? type))
		{
			case MetadataType.Boolean or MetadataType.Char or MetadataType.SByte or MetadataType.Byte or MetadataType.Int16 or MetadataType.UInt16
				or MetadataType.Int32 or MetadataType.UInt32:
				il.Emit(OpCodes.Ldc_I4_0);
				break;
			case MetadataType.Int64 or MetadataType.UInt64:
				il.Emit(OpCodes.Ldc_I8, 0L);
				break;
			case MetadataType.Single:
				il.Emit(OpCodes.Ldc_R4, 0f);
				break;
			case MetadataType.Double:
				il.Emit(OpCodes.Ldc_R8, 0d);
				break;
			case MetadataType.IntPtr or MetadataType.UIntPtr:
				il.Emit(OpCodes.Ldc_I4_0);
				il.Emit(OpCodes.Conv_I);
				break;
			default:
				il.Emit(OpCodes.Ldloc, GetLocal(method, type, locals));
				break;
		}
	}

	// One local per type, by CecilTypes.GetIdentity: same-named types of different assemblies get their own.
	private static VariableDefinition GetLocal(MethodDefinition method, TypeReference type, Dictionary<string, VariableDefinition> locals)
	{
		string identity = CecilTypes.GetIdentity(type);

		if (!locals.TryGetValue(identity, out var local))
		{
			local = new VariableDefinition(type);
			method.Body.Variables.Add(local);
			locals.Add(identity, local);
		}

		return local;
	}
}
