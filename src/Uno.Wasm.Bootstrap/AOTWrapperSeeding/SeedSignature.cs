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
using System.Linq;
using Mono.Cecil;

namespace Uno.Wasm.Bootstrap.AOTWrapperSeeding;

/// <summary>
/// A method signature to seed an interpreter entry wrapper for, with a method that has it.
/// </summary>
internal sealed record SeedSignature(bool HasThis, TypeReference ReturnType, IReadOnlyList<TypeReference> ParameterTypes, string Key, SeedOrigin Origin)
{
	/// <summary>
	/// Returns the seed for a method of this signature, or <see langword="null"/> if it has types a seed can't be emitted for.
	/// </summary>
	public static SeedSignature? From(bool hasThis, TypeReference returnType, IReadOnlyList<TypeReference> parameterTypes, SeedOrigin origin)
	{
		returnType = CecilTypes.StripModifiers(returnType);
		parameterTypes = parameterTypes.Select(CecilTypes.StripModifiers).ToList();

		if (!IsSupported(returnType, true) || !parameterTypes.All(t => IsSupported(t, false)))
		{
			return null;
		}

		if (!CecilTypes.CanResolve(returnType) || !parameterTypes.All(CecilTypes.CanResolve))
		{
			return null;
		}

		string key = (hasThis ? "instance " : "static ") + Normalize(returnType) + "(" + string.Join(",", parameterTypes.Select(Normalize)) + ")";
		return new SeedSignature(hasThis, returnType, parameterTypes, key, origin);
	}

	// The normalizations the runtime certainly applies (PrimitiveTypes.GetShared: references and pointers to object, byrefs to a native int byref,
	// enums to their underlying type, bool and char to unsigned integers, native integers to 32-bit ones on wasm32, and reference type arguments
	// of generic value types to object); value types and function pointers stay as they are otherwise, assemblies included, so two seeds are only
	// merged when the runtime surely merges them too.
	private static string Normalize(TypeReference type) => PrimitiveTypes.GetShared(type) switch
	{
		MetadataType.ByReference => "byref",
		MetadataType.Object => "object",
		MetadataType elementType => elementType.ToString(),
		null => CecilTypes.GetSharedIdentity(type),
	};

	private static bool IsSupported(TypeReference type, bool isReturn)
	{
		type = CecilTypes.StripModifiers(type);

		if (type is ByReferenceType byRef)
		{
			var element = CecilTypes.StripModifiers(byRef.ElementType);
			return element is not ByReferenceType && IsSupported(element, isReturn);
		}

		if (type.ContainsGenericParameter || type is SentinelType)
		{
			return false;
		}

		if (type is PointerType)
		{
			return true;
		}

		switch (PrimitiveTypes.Get(type))
		{
			case MetadataType.TypedByReference:
				return false;
			case MetadataType.Void:
				return isReturn;
		}

		if (type.FullName is "System.ArgIterator" or "System.RuntimeArgumentHandle")
		{
			return false;
		}

		return type is not GenericInstanceType instance || instance.GenericArguments.All(a => IsSupported(a, false));
	}
}

/// <summary>
/// A method that has a seeded signature, with the generic instance it's a method of, if any. Described only when needed, as most seeds are
/// collected many times over.
/// </summary>
internal readonly record struct SeedOrigin(MethodDefinition Method, TypeReference? DeclaringInstance)
{
	/// <summary>
	/// Returns <c>Namespace.Type&lt;Key&gt;::Method [Assembly]</c>, with the types as C# names them.
	/// </summary>
	public override string ToString() => MemberNames.Method(Method, DeclaringInstance, withParameters: false);
}
