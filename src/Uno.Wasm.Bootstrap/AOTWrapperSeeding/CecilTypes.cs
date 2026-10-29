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

namespace Uno.Wasm.Bootstrap.AOTWrapperSeeding;

/// <summary>
/// Type helpers over Mono.Cecil: resolution that doesn't throw, generic inflation and value type checks.
/// </summary>
internal static class CecilTypes
{
	/// <summary>
	/// Removes custom modifiers and pinning, which don't take part in the runtime's signature normalization.
	/// </summary>
	public static TypeReference StripModifiers(TypeReference type)
	{
		while (true)
		{
			switch (type)
			{
				case RequiredModifierType r:
					type = r.ElementType;
					break;
				case OptionalModifierType o:
					type = o.ElementType;
					break;
				case PinnedType p:
					type = p.ElementType;
					break;
				default:
					return type;
			}
		}
	}

	/// <summary>
	/// Replaces the generic parameters of <paramref name="type"/> with the arguments of the type and method instances, when given.
	/// </summary>
	/// <remarks>
	/// The modifiers of the type itself are removed, and those of the types it's made of when it has generic parameters; the callers remove the
	/// others where they matter.
	/// </remarks>
	public static TypeReference Inflate(TypeReference type, GenericInstanceType? typeContext, GenericInstanceMethod? methodContext)
	{
		type = StripModifiers(type);

		if (!type.ContainsGenericParameter)
		{
			return type;
		}

		switch (type)
		{
			case GenericParameter gp:
				if (gp.Type == GenericParameterType.Type && typeContext != null && gp.Position < typeContext.GenericArguments.Count)
				{
					return StripModifiers(typeContext.GenericArguments[gp.Position]);
				}

				if (gp.Type == GenericParameterType.Method && methodContext != null && gp.Position < methodContext.GenericArguments.Count)
				{
					return StripModifiers(methodContext.GenericArguments[gp.Position]);
				}

				return gp;

			case ByReferenceType byRef:
				return new ByReferenceType(Inflate(byRef.ElementType, typeContext, methodContext));

			case PointerType pointer:
				return new PointerType(Inflate(pointer.ElementType, typeContext, methodContext));

			case ArrayType array:
				return array.IsVector
					? new ArrayType(Inflate(array.ElementType, typeContext, methodContext))
					: new ArrayType(Inflate(array.ElementType, typeContext, methodContext), array.Rank);

			case GenericInstanceType instance:
				var inflated = new GenericInstanceType(instance.ElementType);

				foreach (var argument in instance.GenericArguments)
				{
					inflated.GenericArguments.Add(Inflate(argument, typeContext, methodContext));
				}

				return inflated;

			// The types of a function pointer's signature, with its calling convention; the types without generic parameters keep their
			// modifiers (a calling convention modopt on the return type, for example).
			case FunctionPointerType function:
				var inflatedFunction = new FunctionPointerType
				{
					CallingConvention = function.CallingConvention,
					HasThis = function.HasThis,
					ExplicitThis = function.ExplicitThis,
					ReturnType = function.ReturnType.ContainsGenericParameter ? Inflate(function.ReturnType, typeContext, methodContext) : function.ReturnType,
				};

				foreach (var parameter in function.Parameters)
				{
					var parameterType = parameter.ParameterType;
					inflatedFunction.Parameters.Add(new ParameterDefinition(
						parameterType.ContainsGenericParameter ? Inflate(parameterType, typeContext, methodContext) : parameterType));
				}

				return inflatedFunction;

			default:
				return type;
		}
	}

	/// <summary>
	/// Returns the definition of <paramref name="type"/> (of its element type for generic instances), or <see langword="null"/> when it can't be
	/// resolved.
	/// </summary>
	public static TypeDefinition? TryResolve(TypeReference type)
	{
		try
		{
			return StripModifiers(type).Resolve();
		}
		catch (Exception e) when (IsResolutionFailure(e))
		{
			RecordUnexpectedFailure(type, e);
			return null;
		}
	}

	/// <summary>
	/// Returns the definition of <paramref name="method"/> (of its element method for generic instances), or <see langword="null"/> when it can't
	/// be resolved.
	/// </summary>
	public static MethodDefinition? TryResolve(MethodReference method)
	{
		try
		{
			return method.Resolve();
		}
		catch (Exception e) when (IsResolutionFailure(e))
		{
			RecordUnexpectedFailure(method, e);
			return null;
		}
	}

	// A missing or unreadable assembly, or a reference Cecil can't resolve (InvalidOperationException and NotSupportedException come from
	// malformed or unusual metadata, such as a type reference with an unexpected scope).
	private static bool IsResolutionFailure(Exception e)
		=> e is AssemblyResolutionException or FileNotFoundException or BadImageFormatException or InvalidOperationException or NotSupportedException;

	// An assembly that isn't among the AOT inputs is expected (an app can leave some out of the AOT compilation); unusual metadata isn't. The
	// member is named without resolving it again, with the assembly that references it.
	private static void RecordUnexpectedFailure(MemberReference member, Exception e)
	{
		if (e is InvalidOperationException or NotSupportedException)
		{
			string name = member switch
			{
				TypeReference type => MemberNames.Type(type, true),
				MethodReference method => $"{MemberNames.Type(method.DeclaringType, true)}::{method.Name}",
				_ => member.FullName,
			};

			t_generation?.UnexpectedFailures.Add($"{name} [{member.Module?.Assembly?.Name.Name}]: {e.GetType().Name}: {e.Message}");
		}
	}

	// The generation that runs on this thread, if any (BeginGeneration).
	[ThreadStatic]
	private static Generation? t_generation;

	/// <summary>
	/// Starts a generation on this thread, which lasts until the returned object is disposed: <see cref="GetIdentity"/> keeps the identities of
	/// named types until then, <see cref="UnexpectedResolutionFailures"/> lists the references that failed to resolve, and
	/// <see cref="GetGenerationCache{T}"/> keeps what other classes cache. A generation started during another one ends with the other one in place
	/// again.
	/// </summary>
	public static IDisposable BeginGeneration() => t_generation = new Generation(t_generation);

	/// <summary>
	/// Returns the generation's instance of <typeparamref name="T"/>, which lives as long as the generation, or <see langword="null"/> outside of
	/// a generation.
	/// </summary>
	public static T? GetGenerationCache<T>()
		where T : class, new()
	{
		if (t_generation is not { } generation)
		{
			return null;
		}

		if (!generation.Caches.TryGetValue(typeof(T), out object? cache))
		{
			cache = new T();
			generation.Caches.Add(typeof(T), cache);
		}

		return (T)cache;
	}

	/// <summary>
	/// The references of the current generation that failed to resolve because of malformed or unusual metadata, with why.
	/// </summary>
	public static IReadOnlyCollection<string> UnexpectedResolutionFailures => t_generation?.UnexpectedFailures ?? [];

	// The collector asks for the identities of the same types many times over. The named types are those of the modules, which live as long as
	// the generation; the others are mostly short-lived instances, which a cache would keep alive.
	private sealed class Generation(Generation? previous) : IDisposable
	{
		public Dictionary<TypeReference, string> Identities { get; } = new();

		public SortedSet<string> UnexpectedFailures { get; } = new(StringComparer.Ordinal);

		public Dictionary<Type, object> Caches { get; } = new();

		public void Dispose() => t_generation = previous;
	}

	/// <summary>
	/// Returns a key that tells types apart like the runtime does: the full name with the assembly that defines each named type in it, generic
	/// arguments included. <see cref="TypeReference.FullName"/> leaves the assembly out, so it's the same for same-named types of two assemblies.
	/// </summary>
	public static string GetIdentity(TypeReference type)
	{
		if (t_generation is not { } generation || type is TypeSpecification)
		{
			return ComputeIdentity(type);
		}

		if (!generation.Identities.TryGetValue(type, out string? identity))
		{
			identity = ComputeIdentity(type);
			generation.Identities.Add(type, identity);
		}

		return identity;
	}

	private static string ComputeIdentity(TypeReference type)
	{
		type = StripModifiers(type);

		switch (type)
		{
			case GenericInstanceType instance:
				return GetIdentity(instance.ElementType) + "<" + string.Join(",", instance.GenericArguments.Select(GetIdentity)) + ">";

			// Function pointers are different types when their signatures differ.
			case FunctionPointerType function:
				var parameters = function.Parameters.Select(p => GetIdentity(p.ParameterType));
				return $"fnptr {function.CallingConvention} {GetIdentity(function.ReturnType)}({string.Join(",", parameters)})";

			case GenericParameter:
				return type.FullName;

			case TypeSpecification specification:
				// Arrays, pointers and byrefs: the suffix of the full name after the element type.
				return GetIdentity(specification.ElementType) + type.FullName.Substring(specification.ElementType.FullName.Length);

			default:
				string? assembly = TryResolve(type)?.Module.Assembly?.Name.Name ?? type.Scope?.Name;
				return "[" + assembly + "]" + type.FullName;
		}
	}

	/// <summary>
	/// Returns the identity of the type with the reference type arguments of its generic instances, nested ones included, replaced by object: the
	/// runtime shares the code of such instances, and get_wrapper_shared_type_full in mini-generic-sharing.c gives the same wrappers to generic
	/// value types that only differ in reference type arguments.
	/// </summary>
	public static string GetSharedIdentity(TypeReference type)
	{
		type = StripModifiers(type);

		if (type is not GenericInstanceType instance)
		{
			return GetIdentity(type);
		}

		return $"{GetIdentity(instance.ElementType)}<{string.Join(",", instance.GenericArguments.Select(GetSharedArgumentIdentity))}>";
	}

	/// <summary>
	/// Returns the identity of a generic argument as the runtime shares it: <see cref="GetSharedIdentity"/> for value types, object for references.
	/// </summary>
	public static string GetSharedArgumentIdentity(TypeReference argument) => IsValueType(argument) ? GetSharedIdentity(argument) : "object";

	/// <summary>
	/// Returns whether every named type in <paramref name="type"/> resolves, so that a seed can reference it.
	/// </summary>
	public static bool CanResolve(TypeReference type)
	{
		type = StripModifiers(type);

		return type switch
		{
			GenericParameter => false,
			FunctionPointerType function => CanResolve(function.ReturnType) && function.Parameters.All(p => CanResolve(p.ParameterType)),
			GenericInstanceType instance => TryResolve(instance.ElementType) != null && instance.GenericArguments.All(CanResolve),
			TypeSpecification specification => CanResolve(specification.ElementType),
			_ => TryResolve(type) != null,
		};
	}

	public static bool IsValueType(TypeReference type)
	{
		type = StripModifiers(type);

		if (type is ByReferenceType or PointerType or ArrayType or FunctionPointerType or GenericParameter)
		{
			return false;
		}

		return TryResolve(type)?.IsValueType ?? type.IsValueType;
	}

	/// <summary>
	/// Returns the underlying type of an enum, or <see langword="null"/> when <paramref name="type"/> isn't one.
	/// </summary>
	public static TypeReference? GetEnumUnderlyingType(TypeReference type)
	{
		var definition = TryResolve(type);

		if (definition == null || !definition.IsEnum)
		{
			return null;
		}

		return definition.Fields.FirstOrDefault(f => !f.IsStatic)?.FieldType;
	}

	public static bool IsDelegate(TypeDefinition definition) => definition.BaseType?.FullName is "System.MulticastDelegate" or "System.Delegate";

	public static bool IsByRefLike(TypeDefinition definition)
		=> definition.CustomAttributes.Any(a => a.AttributeType.FullName == "System.Runtime.CompilerServices.IsByRefLikeAttribute");

	/// <summary>
	/// Returns the length of an inline array (a value type with <c>[InlineArray]</c>), or 1 when <paramref name="definition"/> isn't one.
	/// </summary>
	public static int GetInlineArrayLength(TypeDefinition definition)
	{
		var attribute = definition.CustomAttributes.FirstOrDefault(a => a.AttributeType.FullName == "System.Runtime.CompilerServices.InlineArrayAttribute");
		return attribute is { ConstructorArguments.Count: > 0 } && attribute.ConstructorArguments[0].Value is int length ? length : 1;
	}

	/// <summary>
	/// Returns the instance fields of a value type, with their types inflated for generic instances.
	/// </summary>
	public static IEnumerable<TypeReference> GetInstanceFieldTypes(TypeReference type, TypeDefinition definition)
	{
		var context = StripModifiers(type) as GenericInstanceType;

		foreach (var field in definition.Fields)
		{
			if (!field.IsStatic)
			{
				yield return Inflate(field.FieldType, context, null);
			}
		}
	}
}
