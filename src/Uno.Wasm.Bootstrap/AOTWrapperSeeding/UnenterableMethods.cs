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
using Mono.Cecil;

namespace Uno.Wasm.Bootstrap.AOTWrapperSeeding;

/// <summary>
/// Finds the methods one of whose parameters keeps its flags (see <see cref="WrapperSignature.KeepsParameterAttributes"/>): whatever the seeding
/// does, the runtime can't find the AOT-compiled wrapper through which compiled code enters the interpreter for such a method, nor the one through
/// which interpreted code calls it once it's compiled. The runtime enters the implementation of a method, whose parameters have their own flags, so
/// only methods with a body count. The flags of a return value don't matter: the wrapper caches only hash the return type's element type.
/// </summary>
internal static class UnenterableMethods
{
	/// <param name="Method">The method, as <c>Namespace.Type&lt;T&gt;::Method(int, List&lt;T&gt;) [Assembly]</c>, with the types as C# names them.</param>
	/// <param name="Parameter">
	/// The parameter that keeps its flags: <c>parameter 9 ('value')</c>, or <c>parameter 9</c> when the assembly has no parameter names, as
	/// trimmed assemblies don't.
	/// </param>
	/// <param name="Flags">What gives the parameter its flags, as C# writes it: <c>a default value</c>, <c>an [Optional] attribute</c>, ...</param>
	/// <param name="TypeArgument">
	/// When the type of the parameter is a generic parameter, the type argument that keeps the flags: inflate_generic_type in class.c gives the
	/// type argument the flags of the parameter.
	/// </param>
	/// <param name="Type">The struct or function pointer type that keeps the flags, as C# names it.</param>
	/// <param name="IsFunctionPointer">Whether <paramref name="Type"/> is a function pointer, rather than a struct.</param>
	/// <param name="ParameterCount">
	/// The method's number of parameters: above <see cref="WrapperSignature.MaxInterpEntryArgs"/>, the runtime needs the wrapper as soon as it
	/// resolves the method, rather than only when the method is called.
	/// </param>
	/// <param name="IsVirtual">Whether the method is a virtual instance method, which the IMT slots of interfaces can hold.</param>
	public sealed record Finding(
		string Method,
		string Parameter,
		string Flags,
		string? TypeArgument,
		string Type,
		bool IsFunctionPointer,
		int ParameterCount,
		bool IsVirtual);

	/// <param name="Member">The method, as <see cref="Finding.Method"/> has it, or the type whose methods couldn't be read.</param>
	/// <param name="Reason">Why it couldn't be checked.</param>
	public sealed record Undetermined(string Member, string Reason);

	/// <param name="Struct">The struct that the runtime can't map (<see cref="WrapperSignature.FindUnsharableStruct"/>), with why.</param>
	/// <param name="Method">The method, as <see cref="SeedOrigin"/> names it.</param>
	public sealed record UnsharableMethod(WrapperSignature.UnsharableStruct Struct, string Method);

	/// <summary>
	/// Adds the methods of the module to <paramref name="found"/>, those it can't tell about, with why, to <paramref name="undetermined"/>, and
	/// those it can't tell about because the runtime can't map a struct (a cyclic layout, or 16 type arguments or more) to
	/// <paramref name="unsharable"/>.
	/// </summary>
	public static void Find(
		ModuleDefinition module,
		ICollection<Finding> found,
		ICollection<Undetermined> undetermined,
		ICollection<UnsharableMethod> unsharable)
	{
		foreach (var type in module.GetTypes())
		{
			try
			{
				foreach (var method in type.Methods)
				{
					if (method.HasBody)
					{
						Check(method, null, null, found, undetermined, unsharable);
					}
				}
			}
			catch (Exception e) when (e is not OutOfMemoryException)
			{
				// Metadata the reader can't handle, in the methods of the type or their signatures.
				undetermined.Add(new Undetermined($"{MemberNames.Type(type, true)} [{module.Assembly?.Name.Name}]", Describe(e)));
			}
		}
	}

	/// <summary>
	/// Adds the methods whose generic parameters keep their flags with the type arguments of the instances, among the instances of generic code
	/// with such parameters that the collector followed (<see cref="SignatureCollector.FlaggedMethodInstances"/>), once per method.
	/// </summary>
	public static void Find(
		IEnumerable<SignatureCollector.MethodInstance> instances,
		ICollection<Finding> found,
		ICollection<Undetermined> undetermined,
		ICollection<UnsharableMethod> unsharable)
	{
		var checkedMethods = new HashSet<MethodDefinition>();

		foreach (var instance in instances)
		{
			var method = instance.Definition;

			if (!checkedMethods.Contains(method)
				&& Check(method, instance.TypeContext, instance.MethodContext, found, undetermined, unsharable))
			{
				checkedMethods.Add(method);
			}
		}
	}

	// Checks the parameters of a method, or with the generic arguments of an instance those whose type is a generic parameter, and tells whether
	// the method was added to any collection. A method that can't be checked only skips itself.
	private static bool Check(
		MethodDefinition method, GenericInstanceType? typeContext, GenericInstanceMethod? methodContext, ICollection<Finding> found,
		ICollection<Undetermined> undetermined, ICollection<UnsharableMethod> unsharable)
	{
		bool isInstance = typeContext != null || methodContext != null;

		try
		{
			// Most methods have no flags: they're checked without allocating.
			if (!HasFlags(method))
			{
				return false;
			}

			foreach (var parameter in method.Parameters)
			{
				bool isGeneric = CecilTypes.StripModifiers(parameter.ParameterType) is GenericParameter;

				if (parameter.Attributes == ParameterAttributes.None || isGeneric != isInstance)
				{
					continue;
				}

				var type = isGeneric ? CecilTypes.Inflate(parameter.ParameterType, typeContext, methodContext) : parameter.ParameterType;

				if (WrapperSignature.KeepsParameterAttributes(type))
				{
					string typeName = MemberNames.Type(type, true);
					found.Add(new Finding(
						MemberNames.Method(method),
						Describe(parameter),
						DescribeFlags(parameter.Attributes),
						isGeneric ? typeName : null,
						typeName,
						CecilTypes.StripModifiers(type) is FunctionPointerType,
						method.Parameters.Count,
						method is { IsVirtual: true, IsStatic: false }));
					return true;
				}
			}
		}
		catch (WrapperSignature.UnsharableStructException e)
		{
			// The runtime can't make the method's wrapper either (WrapperSignature.FindUnsharableStruct): UNOWASM010 reports it with the struct.
			unsharable.Add(new UnsharableMethod(new(e.Root, e.Reason), MemberNames.Method(method, typeContext, withParameters: false)));
			return true;
		}
		catch (Exception e) when (e is not OutOfMemoryException)
		{
			undetermined.Add(new Undetermined(MemberNames.Method(method), Describe(e)));
			return true;
		}

		return false;
	}

	private static bool HasFlags(MethodDefinition method)
	{
		var parameters = method.Parameters;

		for (int i = 0; i < parameters.Count; i++)
		{
			if (parameters[i].Attributes != ParameterAttributes.None)
			{
				return true;
			}
		}

		return false;
	}

	// What gives a parameter its flags, as C# writes it: a default value, which comes with [Optional], or the attributes.
	private static string DescribeFlags(ParameterAttributes attributes)
	{
		if ((attributes & ParameterAttributes.HasDefault) != 0)
		{
			return "a default value";
		}

		var names = new[]
		{
			(ParameterAttributes.Optional, "[Optional]"),
			(ParameterAttributes.In, "[In]"),
			(ParameterAttributes.Out, "[Out]"),
			(ParameterAttributes.HasFieldMarshal, "[MarshalAs]"),
		}.Where(a => (attributes & a.Item1) != 0).Select(a => a.Item2).ToList();

		return names.Count switch
		{
			0 => "flags",
			1 => (names[0] == "[MarshalAs]" ? "a " : "an ") + names[0] + " attribute",
			_ => string.Join(", ", names.Take(names.Count - 1)) + " and " + names[names.Count - 1] + " attributes",
		};
	}

	// NotSupportedException says why the layout of a type can't be told; others come from metadata the reader can't handle.
	private static string Describe(Exception e) => e is NotSupportedException ? e.Message : $"{e.GetType().Name}: {e.Message}";

	// "parameter 9 ('value')", or "parameter 9" without a name
	private static string Describe(ParameterDefinition parameter)
	{
		string position = "parameter " + (parameter.Index + 1).ToString(CultureInfo.InvariantCulture);
		return string.IsNullOrEmpty(parameter.Name) ? position : $"{position} ('{parameter.Name}')";
	}
}
