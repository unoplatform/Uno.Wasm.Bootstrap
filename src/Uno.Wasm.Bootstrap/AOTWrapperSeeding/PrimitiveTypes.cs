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
using Mono.Cecil;

namespace Uno.Wasm.Bootstrap.AOTWrapperSeeding;

/// <summary>
/// The types that signatures encode as element types (the primitive types, <c>string</c>, <c>object</c> and <c>TypedReference</c>), by full
/// name: references to them can also be type references, which only their name tells apart. The values of <see cref="MetadataType"/> are the
/// element types, which are also the runtime's <c>MONO_TYPE_</c> values.
/// </summary>
internal static class PrimitiveTypes
{
	private static readonly Dictionary<string, MetadataType> ByFullName = new(StringComparer.Ordinal)
	{
		["System.Void"] = MetadataType.Void,
		["System.Boolean"] = MetadataType.Boolean,
		["System.Char"] = MetadataType.Char,
		["System.SByte"] = MetadataType.SByte,
		["System.Byte"] = MetadataType.Byte,
		["System.Int16"] = MetadataType.Int16,
		["System.UInt16"] = MetadataType.UInt16,
		["System.Int32"] = MetadataType.Int32,
		["System.UInt32"] = MetadataType.UInt32,
		["System.Int64"] = MetadataType.Int64,
		["System.UInt64"] = MetadataType.UInt64,
		["System.Single"] = MetadataType.Single,
		["System.Double"] = MetadataType.Double,
		["System.IntPtr"] = MetadataType.IntPtr,
		["System.UIntPtr"] = MetadataType.UIntPtr,
		["System.String"] = MetadataType.String,
		["System.Object"] = MetadataType.Object,
		["System.TypedReference"] = MetadataType.TypedByReference,
	};

	/// <summary>
	/// Returns the element type of <paramref name="type"/>, or <see langword="null"/> when it isn't one of these types.
	/// </summary>
	public static MetadataType? Get(TypeReference type)
		=> type is not TypeSpecification && ByFullName.TryGetValue(type.FullName, out var elementType) ? elementType : null;

	/// <summary>
	/// Returns the type the runtime passes a primitive value type as on wasm32: <c>bool</c> as <c>byte</c>, <c>char</c> as <c>ushort</c>, and
	/// native integers as 32-bit integers (mini_type_get_underlying_type and get_wrapper_shared_type_full in mini-generic-sharing.c).
	/// </summary>
	public static MetadataType GetUnderlying(MetadataType elementType) => elementType switch
	{
		MetadataType.Boolean => MetadataType.Byte,
		MetadataType.Char => MetadataType.UInt16,
		MetadataType.IntPtr => MetadataType.Int32,
		MetadataType.UIntPtr => MetadataType.UInt32,
		_ => elementType,
	};

	/// <summary>
	/// Returns the element type the runtime gives <paramref name="type"/> in wrapper signatures before it looks at value types:
	/// <see cref="MetadataType.ByReference"/> for byrefs, <see cref="MetadataType.Object"/> for references and pointers, and the underlying type
	/// (<see cref="GetUnderlying"/>) of enums and primitive types (mini_get_underlying_type and get_wrapper_shared_type_full in
	/// mini-generic-sharing.c). Returns <see langword="null"/> for function pointers and the other value types, which it keeps or maps by their
	/// layout.
	/// </summary>
	public static MetadataType? GetShared(TypeReference type)
	{
		type = CecilTypes.StripModifiers(type);

		switch (type)
		{
			case ByReferenceType:
				return MetadataType.ByReference;
			case FunctionPointerType:
				return null;
			case PointerType:
				return MetadataType.Object;
		}

		if (!CecilTypes.IsValueType(type))
		{
			return MetadataType.Object;
		}

		return Get(CecilTypes.GetEnumUnderlyingType(type) ?? type) is MetadataType elementType ? GetUnderlying(elementType) : null;
	}

	/// <summary>
	/// Returns the size of a primitive value type on wasm32, which is also its alignment, or <see langword="null"/> for <c>void</c>,
	/// <c>string</c>, <c>object</c> and <c>TypedReference</c>.
	/// </summary>
	public static int? GetSize(MetadataType elementType) => GetUnderlying(elementType) switch
	{
		MetadataType.SByte or MetadataType.Byte => 1,
		MetadataType.Int16 or MetadataType.UInt16 => 2,
		MetadataType.Int32 or MetadataType.UInt32 or MetadataType.Single => 4,
		MetadataType.Int64 or MetadataType.UInt64 or MetadataType.Double => 8,
		_ => null,
	};
}
