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
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Mono.Cecil;

namespace Uno.Wasm.Bootstrap.AOTWrapperSeeding;

/// <summary>
/// Reproduces how the Mono runtime (wasm32, llvmonly) names the interpreter entry wrapper it needs for a method's signature: the gsharedvt_in_sig
/// wrapper for up to 8 parameters (mini_get_underlying_signature and get_wrapper_shared_vtype in mini-generic-sharing.c, mangled by
/// append_mangled_type in aot-compiler.c), and the interp_in wrapper above that (mini_get_interp_in_wrapper and mini_get_underlying_reg_signature
/// in mini-generic-sharing.c, named by get_debug_sym in aot-compiler.c). Only used to verify the build; the AOT compiler normalizes the seeded
/// signatures itself.
/// </summary>
/// <remarks>
/// Checked against the runtime of .NET 10.0.12 (in 9.0.11, the functions are equivalent for these rules; in 11.0 RC2, they're unchanged, but
/// mono_class_layout_fields adds extended layouts, which keep their own type like explicit ones, <see cref="KeepsItsType"/>), in dotnet/runtime
/// at tag v10.0.12:
/// <list type="bullet">
///   <item>https://github.com/dotnet/runtime/blob/v10.0.12/src/mono/mono/mini/mini-generic-sharing.c</item>
///   <item>https://github.com/dotnet/runtime/blob/v10.0.12/src/mono/mono/mini/aot-compiler.c</item>
///   <item>https://github.com/dotnet/runtime/blob/v10.0.12/src/mono/mono/metadata/debug-helpers.c (mono_type_get_desc)</item>
///   <item>https://github.com/dotnet/runtime/blob/v10.0.12/src/mono/mono/metadata/class-init.c (mono_class_layout_fields)</item>
/// </list>
/// The unit tests hold names the AOT compiler of 10.0.12 emitted, and the test app's build checks its names against a real AOT compilation. See
/// "Maintenance" in specs/012-aot-wrapper-seeding/spec.md when a runtime update changes them.
/// </remarks>
internal static class WrapperSignature
{
	public const int MaxInterpEntryArgs = 8;

	/// <summary>
	/// The start of the gsharedvt_in_sig wrapper names this class returns, as they appear in the bitcode symbols after the module's prefix.
	/// </summary>
	public const string GsharedvtInMarker = "gsharedvt_in_sig_";

	/// <summary>
	/// The start of the interp_in wrapper names this class returns, as they appear in the bitcode symbols after the module's prefix.
	/// </summary>
	public const string InterpInMarker = "wrapper_other_object_interp_in";

	// TARGET_SIZEOF_VOID_P and MONO_ABI_ALIGNOF (double) on wasm32
	private const int PointerSize = 4, MaxAlign = 8;

	// get_wrapper_shared_vtype: it collects up to 16 fields (its args array), maps the value types without floating-point or value type fields
	// that are up to 5 ints or longs to that many of them, and gives up beyond the 7 type parameters of Mono.ValueTuple`7.
	private const int MaxSharedFields = 16, MaxRegisterFields = 5, MaxValueTupleArity = 7;

	// get_wrapper_shared_type_full asserts that a generic struct has fewer type arguments than its args array holds (FindUnsharableStruct).
	private const int MaxTypeArguments = 16;

	// Whether char is signed in the AOT compiler, which runs on the build machine: it is on x64, and unsigned on 32-bit and 64-bit Arm under Linux.
	private static readonly bool IsCharSigned = !(RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
		&& RuntimeInformation.ProcessArchitecture is Architecture.Arm or Architecture.Arm64);

	/// <summary>
	/// Returns the symbol name suffix of the gsharedvt_in_sig wrapper the runtime needs to enter the interpreter for a method of this signature, or
	/// <see langword="null"/> when it has more than <see cref="MaxInterpEntryArgs"/> parameters (those use interp_in wrappers instead).
	/// </summary>
	public static string? GetGsharedvtInWrapperName(bool hasThis, TypeReference returnType, IReadOnlyList<TypeReference> parameterTypes)
	{
		if (parameterTypes.Count > MaxInterpEntryArgs)
		{
			return null;
		}

		var sb = new StringBuilder(GsharedvtInMarker);
		sb.Append(Mangle(Shared(returnType)));
		sb.Append('_');

		if (hasThis)
		{
			sb.Append("this_");
		}

		foreach (var parameterType in parameterTypes)
		{
			sb.Append(Mangle(Shared(parameterType)));
		}

		return sb.ToString();
	}

	/// <summary>
	/// Returns the symbol name suffix of the interp_in wrapper the runtime needs to enter the interpreter for a method of this signature, with a key
	/// that tells distinct wrappers apart, or <see langword="null"/> when it has <see cref="MaxInterpEntryArgs"/> parameters or fewer (those use
	/// gsharedvt_in_sig wrappers instead).
	/// </summary>
	/// <remarks>
	/// The name only has the parameter types, so wrappers that differ only in their return type or in having <c>this</c> share it. The AOT compiler
	/// then appends <c>_0</c>, <c>_1</c>, ... to all but one of them.
	/// </remarks>
	public static (string Name, string Key)? GetInterpInWrapper(bool hasThis, TypeReference returnType, IReadOnlyList<TypeReference> parameterTypes)
	{
		if (parameterTypes.Count <= MaxInterpEntryArgs)
		{
			return null;
		}

		var parameterShapes = parameterTypes.Select(Shared).ToList();
		string name = DebugSymbol($"(wrapper other) object:interp_in_generic ({string.Join(",", parameterShapes.Select(DescribeReg))})");

		// Value types keep their assembly in the key: the runtime has distinct wrappers for same-named value types of different assemblies.
		string returnShape = IdentifyReg(Shared(returnType));
		string key = $"{(hasThis ? "instance" : "static")} {returnShape}({string.Join(",", parameterShapes.Select(IdentifyReg))})";
		return (name, key);
	}

	/// <summary>
	/// Returns whether the runtime keeps the flags of a parameter of this type (from a default value, <c>[Optional]</c>, <c>[In]</c>, <c>[Out]</c> or
	/// <c>MarshalAs</c>) in the signatures of the wrappers between compiled and interpreted code, where the AOT compiler leaves them out, so that the
	/// runtime never finds the AOT-compiled wrappers.
	/// </summary>
	/// <remarks>
	/// mono_metadata_parse_method_signature_full in metadata.c gives the parameter types the flags of their Param rows. get_wrapper_shared_type_full
	/// in mini-generic-sharing.c replaces most types with new ones, which drops the flags, but returns function pointers and the value types that
	/// get_wrapper_shared_vtype doesn't map to a Mono.ValueTuple as they are (generic ones are re-inflated, which drops the flags too). The wrapper
	/// caches of mini_get_interp_in_wrapper, mini_get_gsharedvt_in_sig_wrapper and mini_get_gsharedvt_out_sig_wrapper key on mono_signature_hash in
	/// metadata.c, which hashes the parameters' flags through mono_type_hash, and the return type only by its element type, so the flags of a return
	/// value don't matter. The AOT compiler doesn't encode the flags (encode_type in aot-compiler.c), and find_aot_method_in_amodule in aot-runtime.c
	/// only accepts the very wrapper the runtime asked for. When compiled code calls such a method while it's interpreted, above
	/// <see cref="MaxInterpEntryArgs"/> parameters, interp_create_method_pointer_llvmonly in interp.c aborts as soon as the runtime resolves the
	/// method, IMT slots included; with fewer, the runtime uses no_gsharedvt_in_wrapper (mini-runtime.c), and the call fails. When interpreted code
	/// calls it once it's compiled, init_jit_call_info in interp.c aborts.
	/// </remarks>
	/// <exception cref="NotSupportedException">
	/// The layout of the type can't be told: the type or the type of one of its fields doesn't resolve, or the runtime can't map it
	/// (<see cref="UnsharableStructException"/>).
	/// </exception>
	public static bool KeepsParameterAttributes(TypeReference type)
	{
		type = CecilTypes.StripModifiers(type);

		if (type is FunctionPointerType)
		{
			return true;
		}

		if (type is TypeSpecification || PrimitiveTypes.GetShared(type) != null)
		{
			return false;
		}

		var definition = CecilTypes.TryResolve(type) ?? throw new NotSupportedException($"Can't resolve {type.FullName}.");
		return SharedValueType(type, definition, new LayoutWalk(type)) == null;
	}

	/// <summary>
	/// The number of shapes and sizes of value types that the current generation keeps (<see cref="CecilTypes.BeginGeneration"/>), which the
	/// tests read.
	/// </summary>
	internal static int CachedLayoutCount => CecilTypes.GetGenerationCache<LayoutCache>() is { } cache ? cache.Shapes.Count + cache.Sizes.Count : 0;

	/// <summary>
	/// Returns the struct that keeps the runtime from mapping the type, with why, or <see langword="null"/> when the runtime maps it. The AOT
	/// compiler and the runtime then fail when they make the interpreter entry wrapper of a method that takes or returns the struct, or a struct
	/// that holds it, by value:
	/// <list type="bullet">
	///   <item>A struct that holds a struct over itself, such as <c>struct Node { ReadOnlyMemory&lt;Node&gt; Children; }</c>, which C# and the
	///   runtime accept, makes get_wrapper_shared_type_full map its type arguments and fields without end, and they overflow the stack, for
	///   <c>ReadOnlyMemory&lt;Node&gt;</c> too. A struct that holds itself through its fields, in any layout, is invalid metadata, which C#
	///   rejects (CS0523) but IL writers can emit. The types of one cycle have the same root (<see cref="LayoutWalk"/>).</item>
	///   <item>A generic struct with 16 type arguments or more, which C# and the runtime accept, makes get_wrapper_shared_type_full assert
	///   (<c>g_assert (inst->type_argc &lt; 16)</c>) before it maps them, whether it's the type, a type argument of a generic struct or the type of
	///   a field that the mapping reaches.</item>
	/// </list>
	/// </summary>
	public static UnsharableStruct? FindUnsharableStruct(TypeReference type)
		=> FindUnsharable(type, walk => Shared(type, walk, LayoutStep.Start)) ?? FindUnsharable(type, walk => ValueSize(type, walk, LayoutStep.Start));

	// The struct that a layout walk can't map: layouts that can't be told for other reasons are mapped as far as the walk can tell.
	private static UnsharableStruct? FindUnsharable(TypeReference type, Action<LayoutWalk> walk)
	{
		try
		{
			walk(new LayoutWalk(type));
			return null;
		}
		catch (UnsharableStructException e)
		{
			return new UnsharableStruct(e.Root, e.Reason);
		}
		catch (Exception e) when (e is not OutOfMemoryException)
		{
			return null;
		}
	}

	private abstract record Shape;

	// Primitive types after mini_type_get_underlying_type, and object for references.
	private sealed record PrimitiveShape(MetadataType ElementType) : Shape;

	private sealed record ByRefShape : Shape
	{
		public static readonly ByRefShape Instance = new();
	}

	// A function pointer, which get_wrapper_shared_type_full keeps as it is, signature included.
	private sealed record FunctionPointerShape(FunctionPointerType Type) : Shape;

	// A value type, with its name as the runtime writes it, its identity, which tells same-named types of different assemblies apart, and the type
	// itself: a Mono.ValueTuple stand-in for mapped types, the type re-inflated with shared generic arguments otherwise.
	private sealed record StructShape(string Name, string Identity, bool IsByRefLike, TypeReference Type) : Shape;

	// append_mangled_type in aot-compiler.c
	private static string Mangle(Shape shape) => shape switch
	{
		ByRefShape => "bii",
		PrimitiveShape p => p.ElementType switch
		{
			MetadataType.Void => "void",
			MetadataType.SByte => "i1",
			MetadataType.Byte => "u1",
			MetadataType.Int16 => "i2",
			MetadataType.UInt16 => "u2",
			MetadataType.Int32 => "i4",
			MetadataType.UInt32 => "u4",
			MetadataType.Int64 => "i8",
			MetadataType.UInt64 => "u8",
			MetadataType.Single => "fl",
			MetadataType.Double => "do",
			MetadataType.Object => "obj",
			_ => throw new NotSupportedException($"Element type {p.ElementType}."),
		},
		StructShape s => MangleClass(s.Name),
		FunctionPointerShape => MangleClass(Describe(shape)),
		_ => throw new NotSupportedException(),
	};

	private static string MangleClass(string fullName)
	{
		bool isSystem = fullName.StartsWith("System.", StringComparison.Ordinal);
		string name = isSystem ? fullName.Substring("System.".Length) : fullName;
		var escaped = new StringBuilder();

		// The runtime escapes the UTF-8 bytes of the name as C chars, printing (int) c in hex.
		foreach (byte b in Encoding.UTF8.GetBytes(name))
		{
			char c = (char)b;

			if (IsAsciiLetterOrDigit(c))
			{
				escaped.Append(c);
			}
			else if (c == '_')
			{
				escaped.Append("__");
			}
			else
			{
				escaped.Append('_');
				escaped.Append(c == '.' ? "d" : (IsCharSigned ? (sbyte)b : (int)b).ToString("x", CultureInfo.InvariantCulture));
			}
		}

		return "cl" + (isSystem ? "s" : "") + escaped.Length.ToString("x", CultureInfo.InvariantCulture) + "_" + escaped + "_";
	}

	// mono_type_get_desc of the shared types
	private static string Describe(Shape shape) => shape switch
	{
		ByRefShape => "intptr&",
		PrimitiveShape p => TypeNames.GetKeyword(p.ElementType),
		StructShape s => s.Name,
		FunctionPointerShape f => TypeNames.Describe(f.Type),
		_ => throw new NotSupportedException(),
	};

	// get_wrapper_shared_type_reg: after get_wrapper_shared_type, integers up to the pointer size and references become intptr.
	private static string DescribeReg(Shape shape) => shape switch
	{
		PrimitiveShape { ElementType: MetadataType.Void or MetadataType.Int64 or MetadataType.UInt64 or MetadataType.Single or MetadataType.Double }
			=> Describe(shape),
		PrimitiveShape => "intptr",
		_ => Describe(shape),
	};

	private static string Identify(Shape shape) => shape switch
	{
		StructShape s => s.Identity,
		FunctionPointerShape f => CecilTypes.GetIdentity(f.Type),
		_ => Describe(shape),
	};

	private static string IdentifyReg(Shape shape) => shape is PrimitiveShape ? DescribeReg(shape) : Identify(shape);

	// get_debug_sym in aot-compiler.c, applied to mono_method_full_name (method, TRUE). It works on the UTF-8 bytes of the name, so each byte of a
	// non-ASCII character becomes an underscore.
	private static string DebugSymbol(string fullName)
	{
		byte[] name = Encoding.UTF8.GetBytes(fullName);
		var sb = new StringBuilder();

		for (int i = 0; i < name.Length; i++)
		{
			char c = (char)name[i];

			if (i == 0 && c >= '0' && c <= '9')
			{
				sb.Append('_');
			}
			else if (IsAsciiLetterOrDigit(c))
			{
				sb.Append(c);
			}
			else if (c == ' ' && i + 2 < name.Length && name[i + 1] == '(' && name[i + 2] == ')')
			{
				i += 2;
			}
			else if (c == ',' && i + 1 < name.Length && name[i + 1] == ' ')
			{
				sb.Append('_');
				i++;
			}
			else if (c is not ('(' or ')' or '>'))
			{
				sb.Append('_');
			}
		}

		return sb.ToString();
	}

	private static bool IsAsciiLetterOrDigit(char c) => c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9');

	// get_wrapper_shared_type_full after mini_get_underlying_type, with a walk of its own, as FindUnsharableStruct does
	private static Shape Shared(TypeReference type) => Shared(type, new LayoutWalk(type), LayoutStep.Start);

	private static Shape Shared(TypeReference type, LayoutWalk walk, LayoutStep step)
	{
		type = CecilTypes.StripModifiers(type);

		if (type is FunctionPointerType function)
		{
			return new FunctionPointerShape(function);
		}

		switch (PrimitiveTypes.GetShared(type))
		{
			case MetadataType.ByReference:
				return ByRefShape.Instance;
			case MetadataType elementType:
				return new PrimitiveShape(elementType);
		}

		string? identity = walk.Cache != null ? CecilTypes.GetIdentity(type) : null;

		if (identity != null && walk.Cache!.Shapes.TryGetValue(identity, out var cached))
		{
			walk.Skip(type, cached.Depth, cached.Steps);
			return cached.Shape;
		}

		var definition = CecilTypes.TryResolve(type) ?? throw new NotSupportedException($"Can't resolve {type.FullName}.");
		var mark = walk.Mark();
		walk.Enter(type, step);
		var shape = SharedStruct(type, definition, walk);
		walk.Exit();
		var (depth, steps) = walk.Since(mark);

		if (identity != null)
		{
			walk.Cache!.Shapes[identity] = (shape, depth, steps);
		}

		return shape;
	}

	private static StructShape SharedStruct(TypeReference type, TypeDefinition definition, LayoutWalk walk)
	{
		bool isByRefLike = CecilTypes.IsByRefLike(definition);

		// Value types the tuple mapping doesn't apply to keep their own type.
		if (type is not GenericInstanceType instance)
		{
			return SharedValueType(type, definition, walk) ?? new StructShape(TypeNames.Describe(type), CecilTypes.GetIdentity(type), isByRefLike, type);
		}

		// The runtime asserts against too many type arguments before it maps them (FindUnsharableStruct).
		if (instance.GenericArguments.Count >= MaxTypeArguments)
		{
			throw new UnsharableStructException(
				$"{MemberNames.Type(type, true)} has {instance.GenericArguments.Count} type arguments, which the runtime can't map.",
				instance,
				UnsharableReason.TooManyTypeArguments);
		}

		// Generic ones are re-inflated with the shared types of their arguments first, which their fields then map again: in Box<T> over a struct of
		// a byte and an int packed to 5 bytes, T is Mono.ValueTuple`2<byte, int>, and the field of type T maps to Mono.ValueTuple`2<int, int>.
		var arguments = instance.GenericArguments.Select(a => Shared(a, walk, LayoutStep.TypeArgument)).ToList();
		var sharedInstance = new GenericInstanceType(instance.ElementType);

		foreach (var argument in arguments)
		{
			sharedInstance.GenericArguments.Add(ToType(argument));
		}

		return SharedValueType(sharedInstance, definition, walk) ?? new StructShape(
			$"{TypeNames.ClassName(instance.ElementType)}<{string.Join(", ", arguments.Select(Describe))}>",
			$"{CecilTypes.GetIdentity(instance.ElementType)}<{string.Join(", ", arguments.Select(Identify))}>",
			isByRefLike,
			sharedInstance);
	}

	// The type a shape stands for, to re-inflate generic value types with; generic arguments are never byrefs.
	private static TypeReference ToType(Shape shape) => shape switch
	{
		PrimitiveShape p => SharedTypes.GetPrimitive(p.ElementType),
		StructShape s => s.Type,
		FunctionPointerShape f => f.Type,
		_ => throw new NotSupportedException(),
	};

	// get_wrapper_shared_vtype for TARGET_WASM
	private static StructShape? SharedValueType(TypeReference type, TypeDefinition definition, LayoutWalk walk)
	{
		if (KeepsItsType(definition))
		{
			return null;
		}

		var arguments = new List<Shape>();
		bool hasExplicitSize = false;

		// mono_metadata_packing_from_typedef: a ClassLayout row with a packing of 0 gives the type an explicit size.
		if (definition.HasLayoutInfo && definition.PackingSize == 0)
		{
			hasExplicitSize = true;

			switch (definition.ClassSize)
			{
				case 1:
					arguments.Add(new PrimitiveShape(MetadataType.Byte));
					break;
				case 2:
					arguments.Add(new PrimitiveShape(MetadataType.Int16));
					break;
				case 4:
					arguments.Add(new PrimitiveShape(MetadataType.Int32));
					break;
				case 8:
					arguments.Add(new PrimitiveShape(MetadataType.Int64));
					break;
				case 16 or 32:
					arguments.AddRange(Enumerable.Repeat(new PrimitiveShape(MetadataType.Int64), definition.ClassSize / 8));
					break;
				default:
					return null;
			}
		}

		bool hasFloatingPoint = false;

		foreach (var fieldType in CecilTypes.GetInstanceFieldTypes(type, definition))
		{
			var fieldShape = Shared(fieldType, walk, LayoutStep.Field);

			if (fieldShape is StructShape { IsByRefLike: true })
			{
				return null;
			}

			if (!hasExplicitSize)
			{
				arguments.Add(fieldShape);

				if (arguments.Count >= MaxSharedFields)
				{
					break;
				}
			}

			if (fieldShape is PrimitiveShape { ElementType: MetadataType.Single or MetadataType.Double } or StructShape)
			{
				hasFloatingPoint = true;
			}
		}

		if (!hasExplicitSize && !hasFloatingPoint)
		{
			var (size, align, _) = ValueSize(type, walk, LayoutStep.Layout);

			if (align is 4 or 8 && size <= align * MaxRegisterFields)
			{
				arguments.Clear();
				arguments.AddRange(Enumerable.Repeat(new PrimitiveShape(align == 4 ? MetadataType.Int32 : MetadataType.Int64), size / align));
			}
		}

		if (arguments.Count > MaxValueTupleArity)
		{
			return null;
		}

		for (int i = 0; i < arguments.Count; i++)
		{
			if (arguments[i] is ByRefShape)
			{
				arguments[i] = new PrimitiveShape(MetadataType.Int32);
			}
		}

		string tuple = "Mono.ValueTuple" + (arguments.Count == 0 ? "" : "`" + arguments.Count.ToString(CultureInfo.InvariantCulture));
		string name = arguments.Count == 0 ? tuple : $"{tuple}<{string.Join(", ", arguments.Select(Describe))}>";
		string identity = arguments.Count == 0 ? tuple : $"{tuple}<{string.Join(", ", arguments.Select(Identify))}>";

		return new StructShape(name, identity, false, SharedTypes.GetValueTuple(arguments.Select(ToType).ToList()));
	}

	// mono_class_value_size for wasm32 auto and sequential layout (mono_class_layout_fields in class-init.c). get_wrapper_shared_vtype only uses
	// it for the value types it maps that have no floating-point or value type fields, so the fields here are primitives, references and pointers.
	private static (int Size, int Align, bool HasReferences) ValueSize(TypeReference type, LayoutWalk walk, LayoutStep step)
	{
		type = CecilTypes.StripModifiers(type);

		if (type is ByReferenceType or PointerType or FunctionPointerType)
		{
			return (PointerSize, PointerSize, false);
		}

		if (!CecilTypes.IsValueType(type))
		{
			return (PointerSize, PointerSize, true);
		}

		type = CecilTypes.GetEnumUnderlyingType(type) ?? type;

		if (PrimitiveTypes.Get(type) is MetadataType elementType && PrimitiveTypes.GetSize(elementType) is int primitiveSize)
		{
			return (primitiveSize, primitiveSize, false);
		}

		string? identity = walk.Cache != null ? CecilTypes.GetIdentity(type) : null;

		if (identity != null && walk.Cache!.Sizes.TryGetValue(identity, out var cached))
		{
			walk.Skip(type, cached.Depth, cached.Steps);
			return cached.Size;
		}

		var definition = CecilTypes.TryResolve(type) ?? throw new NotSupportedException($"Can't resolve {type.FullName}.");
		var mark = walk.Mark();
		walk.Enter(type, step);
		var size = StructSize(type, definition, walk);
		walk.Exit();
		var (depth, steps) = walk.Since(mark);

		if (identity != null)
		{
			walk.Cache!.Sizes[identity] = (size, depth, steps);
		}

		return size;
	}

	private static (int Size, int Align, bool HasReferences) StructSize(TypeReference type, TypeDefinition definition, LayoutWalk walk)
	{
		int pack = definition.HasLayoutInfo ? definition.PackingSize : 0;
		int explicitSize = definition.HasLayoutInfo ? definition.ClassSize : 0;
		int inlineArrayLength = CecilTypes.GetInlineArrayLength(definition);
		bool hasReferences = false;
		int size = 0;

		// The packing raises the alignment of the type, up to that of a double.
		int align = Math.Min(MaxAlign, Math.Max(1, pack));

		foreach (var fieldType in CecilTypes.GetInstanceFieldTypes(type, definition))
		{
			var (fieldSize, fieldAlign, fieldHasReferences) = ValueSize(fieldType, walk, LayoutStep.Field);

			// The single field of an inline array takes the size of its type times the length.
			fieldSize *= inlineArrayLength;

			if (pack > 0)
			{
				fieldAlign = Math.Min(fieldAlign, pack);
			}

			// The packing doesn't apply to fields with references, which stay aligned to the pointer size.
			if (fieldHasReferences)
			{
				fieldAlign = Math.Max(fieldAlign, PointerSize);
				hasReferences = true;
			}

			size = RoundUp(size, fieldAlign) + fieldSize;
			align = Math.Max(align, fieldAlign);
		}

		// An explicit size is a minimum, which isn't rounded up to the alignment.
		return (explicitSize > 0 ? Math.Max(size, explicitSize) : RoundUp(size, align), align, hasReferences);
	}

	private static int RoundUp(int size, int align) => (size + align - 1) / align * align;

	// get_wrapper_shared_vtype only maps auto and sequential layouts, which are the same for value types under mono: explicit layouts, and the
	// extended layouts that [ExtendedLayout] gives C structs and unions in .NET 11 (TYPE_ATTRIBUTE_EXTENDED_LAYOUT), keep their own type.
	private static bool KeepsItsType(TypeDefinition definition)
		=> (definition.Attributes & TypeAttributes.LayoutMask) is not (TypeAttributes.AutoLayout or TypeAttributes.SequentialLayout);

	// How a layout walk reaches a value type: where it starts, through a type argument or a field, or as the struct whose size SharedValueType
	// computes, which it entered already.
	private enum LayoutStep
	{
		Start,
		TypeArgument,
		Field,
		Layout,
	}

	// The value types a layout walk goes through, which cyclic layouts make endless (FindUnsharableStruct): without a bound, the walk would overflow the
	// stack, which no catch can handle. Value types nest far less deep, fields and type arguments included. A generic struct that expands without
	// repeating itself takes ever more steps instead, which only invalid metadata has: the runtime's type loader rejects it.
	private sealed class LayoutWalk(TypeReference start)
	{
		private const int MaxDepth = 64, MaxSteps = 100_000;

		private readonly List<(TypeReference Type, LayoutStep Step)> _path = [];
		private int _steps, _peak;

		// The shapes and sizes of the generation's value types, which seeds share, and which large ones take long to walk again.
		public LayoutCache? Cache { get; } = CecilTypes.GetGenerationCache<LayoutCache>();

		public void Enter(TypeReference type, LayoutStep step)
		{
			_path.Add((type, step));
			_peak = Math.Max(_peak, _path.Count);

			if (_path.Count > MaxDepth)
			{
				throw TooDeep(type);
			}

			if (++_steps > MaxSteps)
			{
				throw TooLarge(type);
			}
		}

		// A walk that throws isn't used again, so only the walks that return leave.
		public void Exit() => _path.RemoveAt(_path.Count - 1);

		// Starts measuring the walk of a type for the cache: how much deeper it goes, and how many steps it takes (Since).
		public (int Depth, int Peak, int Steps) Mark()
		{
			var mark = (_path.Count, _peak, _steps);
			_peak = _path.Count;
			return mark;
		}

		public (int Depth, int Steps) Since((int Depth, int Peak, int Steps) mark)
		{
			var walked = (_peak - mark.Depth, _steps - mark.Steps);
			_peak = Math.Max(_peak, mark.Peak);
			return walked;
		}

		// Counts the depth and the steps of a walk that the cache spares, so that each bound fires where walking again would fire it. When the spared
		// walk would cross both, the depth bound fires, where walking again might cross the steps bound first: that needs more than 100,000 steps
		// 64 value types deep, which no real struct has.
		public void Skip(TypeReference type, int depth, int steps)
		{
			if (_path.Count + depth > MaxDepth)
			{
				throw TooDeep(type);
			}

			_peak = Math.Max(_peak, _path.Count + depth);

			if ((_steps += steps) > MaxSteps)
			{
				throw TooLarge(type);
			}
		}

		private UnsharableStructException TooDeep(TypeReference type)
			=> new(
				$"The layout of {MemberNames.Type(type, true)} holds a struct over itself, or nests more than {MaxDepth} value types.",
				FindRoot(),
				UnsharableReason.CyclicLayout);

		private static NotSupportedException TooLarge(TypeReference type)
			=> new($"The layout of {MemberNames.Type(type, true)} is too large to reproduce.");

		// The struct that holds a struct over itself: of the types that the walk goes round, the first, in the order of their identities, whose field
		// holds the next one, so that every type of a cycle gives the same root. A walk that nests deeper without going round, which only invalid
		// metadata does, gives the type it started from.
		private TypeReference FindRoot()
		{
			var path = _path.Where(p => p.Step != LayoutStep.Layout).ToList();
			var identities = path.Select(p => CecilTypes.GetIdentity(p.Type)).ToList();

			for (int last = identities.Count - 1; last > 0; last--)
			{
				int first = identities.LastIndexOf(identities[last], last - 1);

				if (first >= 0)
				{
					var holders = Enumerable.Range(first, last - first).Where(i => path[i + 1].Step == LayoutStep.Field).DefaultIfEmpty(first);
					return path[holders.OrderBy(i => identities[i], StringComparer.Ordinal).First()].Type;
				}
			}

			return start;
		}
	}

	// The shapes and sizes of value types, by identity, with the depth and the steps of their walks (LayoutWalk.Skip). Only the walks that return are
	// kept, and what a type gives doesn't depend on the walk that reaches it.
	private sealed class LayoutCache
	{
		public Dictionary<string, (Shape Shape, int Depth, int Steps)> Shapes { get; } = new(StringComparer.Ordinal);

		public Dictionary<string, ((int Size, int Align, bool HasReferences) Size, int Depth, int Steps)> Sizes { get; } = new(StringComparer.Ordinal);
	}

	/// <summary>
	/// Why the runtime can't map a struct (<see cref="FindUnsharableStruct"/>).
	/// </summary>
	public enum UnsharableReason
	{
		/// <summary>The struct holds a struct over itself: a layout walk went round, or nested deeper than value types do.</summary>
		CyclicLayout,

		/// <summary>The struct is generic, with 16 type arguments or more.</summary>
		TooManyTypeArguments,
	}

	/// <param name="Root">The struct: the root of a cycle, or the generic struct instance with too many type arguments.</param>
	/// <param name="Reason">Why the runtime can't map it.</param>
	public sealed record UnsharableStruct(TypeReference Root, UnsharableReason Reason);

	/// <summary>
	/// A layout walk reached a struct that the runtime can't map (<see cref="FindUnsharableStruct"/>).
	/// </summary>
	internal sealed class UnsharableStructException(string message, TypeReference root, UnsharableReason reason) : NotSupportedException(message)
	{
		/// <inheritdoc cref="UnsharableStruct.Root"/>
		public TypeReference Root { get; } = root;

		/// <inheritdoc cref="UnsharableStruct.Reason"/>
		public UnsharableReason Reason { get; } = reason;
	}

	// Stand-ins for the types that get_wrapper_shared_type_full re-inflates generic value types with: the primitive types, and the Mono.ValueTuple
	// types of the runtime's CoreLib, sequential value types with fields Item1, Item2, ... of their type parameters. None of them resolves among
	// the AOT inputs.
	private static class SharedTypes
	{
		private static readonly ModuleDefinition Module = ModuleDefinition.CreateModule("Uno.Wasm.AotWrapperSharedTypes", ModuleKind.Dll);

		private static readonly Dictionary<MetadataType, TypeReference> Primitives = new[]
		{
			MetadataType.SByte, MetadataType.Byte, MetadataType.Int16, MetadataType.UInt16, MetadataType.Int32, MetadataType.UInt32, MetadataType.Int64,
			MetadataType.UInt64, MetadataType.Single, MetadataType.Double, MetadataType.Object,
		}.ToDictionary(t => t, t => new TypeReference("System", t.ToString(), Module, null, t != MetadataType.Object));

		// Mono.ValueTuple, Mono.ValueTuple`1, ... Mono.ValueTuple`7
		private static readonly TypeDefinition[] ValueTuples = Enumerable.Range(0, 8).Select(CreateValueTuple).ToArray();

		public static TypeReference GetPrimitive(MetadataType elementType) => Primitives[elementType];

		public static TypeReference GetValueTuple(IReadOnlyList<TypeReference> arguments)
		{
			if (arguments.Count == 0)
			{
				return ValueTuples[0];
			}

			var tuple = new GenericInstanceType(ValueTuples[arguments.Count]);

			foreach (var argument in arguments)
			{
				tuple.GenericArguments.Add(argument);
			}

			return tuple;
		}

		private static TypeDefinition CreateValueTuple(int count)
		{
			string name = count == 0 ? "ValueTuple" : "ValueTuple`" + count.ToString(CultureInfo.InvariantCulture);
			var valueType = new TypeReference("System", "ValueType", Module, null);
			var tuple = new TypeDefinition("Mono", name, TypeAttributes.SequentialLayout | TypeAttributes.Sealed, valueType);

			// The C# compiler gives empty structs a size of 1, which is what get_wrapper_shared_vtype maps Mono.ValueTuple by.
			if (count == 0)
			{
				tuple.PackingSize = 0;
				tuple.ClassSize = 1;
			}

			for (int i = 1; i <= count; i++)
			{
				string index = i.ToString(CultureInfo.InvariantCulture);
				var parameter = new GenericParameter("T" + index, tuple);
				tuple.GenericParameters.Add(parameter);
				tuple.Fields.Add(new FieldDefinition("Item" + index, FieldAttributes.Public, parameter));
			}

			Module.Types.Add(tuple);
			return tuple;
		}
	}
}
