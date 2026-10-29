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

using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Mono.Cecil;

namespace Uno.Wasm.Bootstrap.AOTWrapperSeeding;

/// <summary>
/// Names types and methods in build messages as C# does: <c>Namespace.Type&lt;T&gt;::Method&lt;U&gt;(int, in Key) [Assembly]</c>.
/// </summary>
internal static class MemberNames
{
	// The metadata name of a local function, <Method>g__Local|1_0, which is shown as Method.Local.
	private static readonly Regex LocalFunction = new(@"^<([^>]+)>g__([^|]+)\|", RegexOptions.CultureInvariant);

	/// <summary>
	/// Returns the type as C# names it, with the namespace of its outermost named type when asked: <c>List&lt;string&gt;</c>,
	/// <c>Outer&lt;T&gt;.Inner</c>, <c>int[]</c>, <c>int?</c>, <c>(int, string)</c>, <c>delegate*&lt;int, void&gt;</c>.
	/// </summary>
	public static string Type(TypeReference type, bool withNamespace = false)
	{
		var sb = new StringBuilder();
		AppendType(sb, type, withNamespace);
		return sb.ToString();
	}

	/// <summary>
	/// Returns <c>Namespace.Type&lt;T&gt;::Method&lt;U&gt;(int, ref T) [Assembly]</c>, with the generic arguments of the declaring instance when
	/// given, and without the parameters when asked.
	/// </summary>
	public static string Method(MethodDefinition method, TypeReference? declaringInstance = null, bool withParameters = true)
	{
		var sb = new StringBuilder();
		AppendType(sb, declaringInstance ?? method.DeclaringType, true);
		sb.Append("::").Append(LocalFunction.Match(method.Name) is { Success: true } local ? $"{local.Groups[1].Value}.{local.Groups[2].Value}" : method.Name);

		if (method.HasGenericParameters)
		{
			sb.Append('<').Append(string.Join(", ", method.GenericParameters.Select(p => p.Name))).Append('>');
		}

		if (withParameters)
		{
			AppendParameters(sb, method.Parameters);
		}

		return sb.Append(" [").Append(method.Module.Assembly.Name.Name).Append(']').ToString();
	}

	/// <summary>
	/// Returns a method as <see cref="Method"/> names it, or <c>Namespace.Type&lt;T&gt; [Assembly]</c>.
	/// </summary>
	public static string Member(MemberReference member) => member switch
	{
		MethodDefinition method => Method(method),
		TypeReference type => $"{Type(type, true)} [{CecilTypes.TryResolve(type)?.Module.Assembly?.Name.Name ?? type.Scope?.Name}]",
		_ => member.FullName,
	};

	private static void AppendType(StringBuilder sb, TypeReference type, bool withNamespace)
	{
		type = CecilTypes.StripModifiers(type);

		switch (type)
		{
			case ByReferenceType byRef:
				sb.Append("ref ");
				AppendType(sb, byRef.ElementType, withNamespace);
				return;

			case PointerType pointer:
				AppendType(sb, pointer.ElementType, withNamespace);
				sb.Append('*');
				return;

			case ArrayType array:
				AppendType(sb, array.ElementType, withNamespace);
				sb.Append('[').Append(',', array.Rank - 1).Append(']');
				return;

			case FunctionPointerType function:
				sb.Append(function.CallingConvention == MethodCallingConvention.Default ? "delegate*<" : "delegate* unmanaged<");

				foreach (var parameter in function.Parameters)
				{
					AppendParameter(sb, parameter);
					sb.Append(", ");
				}

				AppendType(sb, function.ReturnType, false);
				sb.Append('>');
				return;

			case GenericParameter parameter:
				sb.Append(parameter.Name);
				return;

			case GenericInstanceType { ElementType.Namespace: "System" } instance when IsNullable(instance) || IsTuple(instance):
				var arguments = instance.GenericArguments;

				if (IsNullable(instance))
				{
					AppendType(sb, arguments[0], false);
					sb.Append('?');
					return;
				}

				sb.Append('(');

				for (int i = 0; i < arguments.Count; i++)
				{
					sb.Append(i > 0 ? ", " : "");
					AppendType(sb, arguments[i], false);
				}

				sb.Append(')');
				return;

			case GenericInstanceType instance:
				AppendNamed(sb, instance.ElementType, instance.GenericArguments, withNamespace);
				return;
		}

		if (PrimitiveTypes.Get(type) is MetadataType elementType && GetKeyword(elementType) is string keyword)
		{
			sb.Append(keyword);
			return;
		}

		AppendNamed(sb, type, type.GenericParameters.Cast<TypeReference>().ToList(), withNamespace);
	}

	private static bool IsNullable(GenericInstanceType instance) => instance.ElementType.Name == "Nullable`1";

	// ValueTuple`2 to ValueTuple`7: an eighth argument holds the rest of a longer tuple.
	private static bool IsTuple(GenericInstanceType instance)
		=> instance.ElementType.Name.StartsWith("ValueTuple`", System.StringComparison.Ordinal) && instance.GenericArguments.Count is >= 2 and <= 7;

	// Outer<A>.Inner<B>: a nested type has the generic parameters of the types it's nested in first, then its own.
	private static void AppendNamed(StringBuilder sb, TypeReference type, IList<TypeReference> arguments, bool withNamespace)
	{
		var chain = new List<TypeReference>();

		for (var current = type; current != null; current = current.DeclaringType)
		{
			chain.Insert(0, current);
		}

		if (withNamespace && !string.IsNullOrEmpty(chain[0].Namespace))
		{
			sb.Append(chain[0].Namespace).Append('.');
		}

		int used = 0;

		for (int i = 0; i < chain.Count; i++)
		{
			string name = chain[i].Name;
			int tick = name.IndexOf('`');
			int arity = tick < 0 || !int.TryParse(name.Substring(tick + 1), NumberStyles.None, CultureInfo.InvariantCulture, out int count) ? 0 : count;
			sb.Append(i > 0 ? "." : "").Append(tick < 0 ? name : name.Substring(0, tick));

			if (arity > 0 && used + arity <= arguments.Count)
			{
				sb.Append('<');

				for (int j = 0; j < arity; j++)
				{
					sb.Append(j > 0 ? ", " : "");
					AppendType(sb, arguments[used + j], false);
				}

				sb.Append('>');
				used += arity;
			}
		}
	}

	private static void AppendParameters(StringBuilder sb, IList<ParameterDefinition> parameters)
	{
		sb.Append('(');

		for (int i = 0; i < parameters.Count; i++)
		{
			sb.Append(i > 0 ? ", " : "");
			AppendParameter(sb, parameters[i]);
		}

		sb.Append(')');
	}

	// out T and in T, which signatures pass as byrefs.
	private static void AppendParameter(StringBuilder sb, ParameterDefinition parameter)
	{
		if (CecilTypes.StripModifiers(parameter.ParameterType) is ByReferenceType byRef)
		{
			sb.Append(parameter.IsOut ? "out " : parameter.IsIn ? "in " : "ref ");
			AppendType(sb, byRef.ElementType, false);
		}
		else
		{
			AppendType(sb, parameter.ParameterType, false);
		}
	}

	private static string? GetKeyword(MetadataType elementType) => elementType switch
	{
		MetadataType.Void => "void",
		MetadataType.Boolean => "bool",
		MetadataType.Char => "char",
		MetadataType.SByte => "sbyte",
		MetadataType.Byte => "byte",
		MetadataType.Int16 => "short",
		MetadataType.UInt16 => "ushort",
		MetadataType.Int32 => "int",
		MetadataType.UInt32 => "uint",
		MetadataType.Int64 => "long",
		MetadataType.UInt64 => "ulong",
		MetadataType.Single => "float",
		MetadataType.Double => "double",
		MetadataType.IntPtr => "nint",
		MetadataType.UIntPtr => "nuint",
		MetadataType.String => "string",
		MetadataType.Object => "object",
		_ => null,
	};
}
