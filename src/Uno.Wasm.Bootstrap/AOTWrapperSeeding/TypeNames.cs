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
using System.Text;
using Mono.Cecil;

namespace Uno.Wasm.Bootstrap.AOTWrapperSeeding;

/// <summary>
/// Formats types the way the Mono runtime's mono_type_get_desc does (debug-helpers.c), which is how wrapper names spell types.
/// </summary>
internal static class TypeNames
{
	/// <summary>
	/// Returns the class name as the runtime's append_class_name writes it: <c>Namespace.Outer/Inner</c>.
	/// </summary>
	public static string ClassName(TypeReference type)
	{
		if (type.DeclaringType != null)
		{
			return ClassName(type.DeclaringType) + "/" + type.Name;
		}

		return string.IsNullOrEmpty(type.Namespace) ? type.Name : type.Namespace + "." + type.Name;
	}

	/// <summary>
	/// Returns the name mono_type_get_desc gives a type that signatures encode as an element type (see <see cref="PrimitiveTypes"/>).
	/// </summary>
	public static string GetKeyword(MetadataType elementType) => elementType switch
	{
		MetadataType.Void => "void",
		MetadataType.Boolean => "bool",
		MetadataType.Char => "char",
		MetadataType.SByte => "sbyte",
		MetadataType.Byte => "byte",
		MetadataType.Int16 => "int16",
		MetadataType.UInt16 => "uint16",
		MetadataType.Int32 => "int",
		MetadataType.UInt32 => "uint",
		MetadataType.Int64 => "long",
		MetadataType.UInt64 => "ulong",
		MetadataType.Single => "single",
		MetadataType.Double => "double",
		MetadataType.String => "string",
		MetadataType.IntPtr => "intptr",
		MetadataType.UIntPtr => "uintptr",
		MetadataType.Object => "object",
		MetadataType.TypedByReference => "typedbyref",
		_ => throw new NotSupportedException($"Element type {elementType}."),
	};

	public static string Describe(TypeReference type)
	{
		var sb = new StringBuilder();
		Append(sb, type);
		return sb.ToString();
	}

	private static void Append(StringBuilder sb, TypeReference type)
	{
		type = CecilTypes.StripModifiers(type);
		bool byRef = type is ByReferenceType;

		if (type is ByReferenceType byRefType)
		{
			type = CecilTypes.StripModifiers(byRefType.ElementType);
		}

		if (PrimitiveTypes.Get(type) is MetadataType elementType)
		{
			sb.Append(GetKeyword(elementType));
		}
		else if (type is GenericParameter)
		{
			sb.Append(type.Name);
		}
		else if (type is PointerType pointer)
		{
			Append(sb, pointer.ElementType);
			sb.Append('*');
		}
		else if (type is FunctionPointerType)
		{
			sb.Append("*()");
		}
		else if (type is ArrayType array)
		{
			Append(sb, array.ElementType);

			if (array.IsVector)
			{
				sb.Append("[]");
			}
			else
			{
				sb.Append('[');
				sb.Append(',', array.Rank - 1);
				sb.Append(']');
			}
		}
		else if (type is GenericInstanceType instance)
		{
			sb.Append(ClassName(instance.ElementType));
			sb.Append('<');

			for (int i = 0; i < instance.GenericArguments.Count; i++)
			{
				if (i > 0)
				{
					sb.Append(", ");
				}

				Append(sb, instance.GenericArguments[i]);
			}

			sb.Append('>');
		}
		else
		{
			sb.Append(ClassName(type));
		}

		if (byRef)
		{
			sb.Append('&');
		}
	}
}
