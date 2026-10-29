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
using System.Threading;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Uno.Wasm.Bootstrap.AOTWrapperSeeding;

/// <summary>
/// Collects the signatures of the methods the runtime may have to enter the interpreter for from AOT-compiled code without a call site that names
/// the signature, which is the only way the AOT compiler learns which interpreter entry wrappers to emit:
/// <list type="bullet">
///   <item>interface methods (in llvmonly mode an IMT slot is initialized for every interface method that hashes to it, not just the one called),
///   including default implementations, of every type, of the generic types the code constructs, and of the interface instances call sites
///   name;</item>
///   <item>delegate targets (the target is resolved when AOT-compiled code creates the delegate), read from the <c>ldftn</c> and <c>ldvirtftn</c>
///   instructions;</item>
///   <item>virtual methods declared with the generic parameters of their type, of generic instances and their generic base types, and the
///   instances of generic virtual methods that the code calls: vtable slots are only initialized when compiled code calls them, and the AOT
///   compiler emits the wrappers of the signatures its call sites name, but code compiled once for every value type argument (gsharedvt) names
///   signatures with generic parameters instead of the value types.</item>
/// </list>
/// The generic types the code constructs include those that the code of generic methods and of generic instances constructs, read from their
/// bodies with their generic arguments: generic methods and methods of generic types that closed code calls, and the methods of generic instances
/// that the runtime reaches without a call that names them (see <see cref="IsReachedIndirectly"/>). Signatures that mention generic parameters
/// are skipped: the runtime only needs wrappers for concrete signatures.
/// </summary>
internal sealed class SignatureCollector
{
	// The deepest nesting of generic instances that the AOT compiler instantiates (check_type_depth in aot-compiler.c).
	private const int MaxGenericNesting = 8;

	// A bound on the base types and interfaces walked from a type, which only invalid metadata (a cycle) can reach.
	private const int MaxBaseTypes = 64;

	/// <summary>
	/// The most named types that the generic instances the collector follows hold, nested generic arguments included, in the form the runtime shares
	/// (<see cref="GetSharedGenericSize"/>), and the most that the generic arguments of a method instance hold together. Recursive generic code can
	/// make ever larger instances up to the nesting bound: a method Go&lt;A, B&gt; that calls itself over three new pairs of its arguments makes
	/// 13,121 instances of up to 767 types, which takes minutes and gigabytes, and 1,129 with this bound. A large app's instances hold up to 18 types.
	/// </summary>
	private const int MaxGenericSize = 64;

	/// <summary>
	/// The most instances of one generic type or method that the collector follows. Recursive generic code can make ever more instances that stay
	/// small: a method Go&lt;A, B&gt; that calls Go&lt;(A, X), B&gt; and Go&lt;A, (B, X)&gt; for three types X makes millions, while a large app
	/// follows up to 3,492 instances of one generic type or method (Func`3).
	/// </summary>
	internal int MaxInstancesPerDefinition { get; init; } = 20_000;

	/// <summary>
	/// The most instances of generic types and methods that the collector follows in all, more than three times the 134,509 of a large app (131
	/// assemblies), in case many generic types or methods reach <see cref="MaxInstancesPerDefinition"/>.
	/// </summary>
	internal int MaxFollowedInstances { get; init; } = 500_000;

	/// <summary>
	/// Stops the collection between two types or methods.
	/// </summary>
	public CancellationToken CancellationToken { get; init; }

	private readonly Dictionary<string, SeedSignature> _seeds = new(StringComparer.Ordinal);

	// Types by CecilTypes.GetIdentity, as same-named types of different assemblies are different types: the types whose methods were collected
	// (AddType), the interface instances whose methods were collected (AddInterface), the generic instances whose methods were collected
	// (AddGenericInstanceMethods), and the generic instances the code names (QueueConstructedType).
	private readonly HashSet<string> _visitedTypes = new(StringComparer.Ordinal);
	private readonly HashSet<string> _visitedInterfaces = new(StringComparer.Ordinal);
	private readonly HashSet<string> _visitedGenericInstances = new(StringComparer.Ordinal);
	private readonly HashSet<string> _queuedTypes = new(StringComparer.Ordinal);

	// The generic instances queued for AddType, by CecilTypes.GetSharedIdentity: one instance stands for all those that only differ in reference type
	// arguments.
	private readonly HashSet<string> _sharedTypes = new(StringComparer.Ordinal);
	private readonly Queue<GenericInstanceType> _constructedTypes = new();

	// The generic methods and methods of generic types that closed code calls (with the identities of their generic arguments as the runtime shares
	// them, which stand for all their instances that only differ in reference type arguments), and the operands of the generic code's bodies that
	// depend on generic parameters.
	private readonly HashSet<(MethodDefinition Method, string Arguments)> _calledMethodInstances = new();
	private readonly Queue<MethodInstance> _methodInstances = new();
	private readonly Dictionary<MethodDefinition, List<Operand>> _genericOperands = new();
	private readonly Dictionary<MethodReference, MethodDefinition?> _resolvedMethods = new();

	// The implementations of generic virtual methods and of generic static abstract members in classes and structs that aren't generic, and the
	// method contexts of the calls to such methods, by GetGenericVirtualKey (IsGenericVirtualImplementation).
	private readonly Dictionary<string, List<MethodDefinition>> _genericVirtualImplementations = new(StringComparer.Ordinal);
	private readonly Dictionary<string, List<GenericInstanceMethod>> _genericVirtualCalls = new(StringComparer.Ordinal);
	private readonly HashSet<(string Key, string Arguments)> _genericVirtualCallKeys = new();

	// The instances followed of each generic type and method definition, and the definitions and code that UNOWASM011 reported (IsWithinLimits).
	private readonly Dictionary<MemberReference, int> _instanceCounts = new();
	private readonly HashSet<MemberReference> _limitedDefinitions = new();
	private readonly HashSet<MemberReference> _limitedOrigins = new();
	private int _followedCount;
	private bool _isFollowedCountReported;

	// The type or method whose code or signatures the collector reads: the definition of a generic instance, not the instance.
	private MemberReference? _origin;

	private int _interfaceMethodCount;
	private int _delegateTargetCount;
	private int _virtualMethodCount;
	private int _constructedTypeCount;
	private int _unsupportedCount;

	private readonly List<string> _skipped = [];
	private readonly HashSet<string> _skippedSet = new(StringComparer.Ordinal);
	private readonly List<string> _limits = [];
	private readonly List<MethodInstance> _flaggedMethodInstances = [];

	/// <param name="InterfaceMethods">The interface methods collected, repeats included.</param>
	/// <param name="DelegateTargets">The delegate targets collected, repeats included.</param>
	/// <param name="VirtualMethods">The virtual methods collected, repeats included.</param>
	/// <param name="ConstructedTypes">The generic instances whose methods were collected.</param>
	/// <param name="Unsupported">The collected methods whose signatures can't be seeded, repeats included.</param>
	public sealed record Statistics(int InterfaceMethods, int DelegateTargets, int VirtualMethods, int ConstructedTypes, int Unsupported);

	public Statistics Collected
		=> new(_interfaceMethodCount, _delegateTargetCount, _virtualMethodCount, _constructedTypeCount, _unsupportedCount);

	public IReadOnlyCollection<SeedSignature> Seeds => _seeds.Values;

	/// <summary>
	/// The types and methods that couldn't be read, with why. Their signatures aren't seeded, but the others are.
	/// </summary>
	public IReadOnlyList<string> Skipped => _skipped;

	/// <summary>
	/// The generic instances that weren't followed because of <see cref="MaxGenericSize"/>, <see cref="MaxInstancesPerDefinition"/> or
	/// <see cref="MaxFollowedInstances"/>: "the generic instances that X uses whose ...", "the generic instances of X past ...", or "the generic
	/// instances past ...".
	/// </summary>
	public IReadOnlyList<string> Limits => _limits;

	/// <summary>
	/// The instances of generic code followed whose methods have a parameter whose type is a generic parameter and which has flags, which the type
	/// argument gets (see <see cref="UnenterableMethods"/>).
	/// </summary>
	public IReadOnlyList<MethodInstance> FlaggedMethodInstances => _flaggedMethodInstances;

	public void AddModule(ModuleDefinition module)
	{
		var types = module.GetTypes().ToList();

		foreach (var type in types)
		{
			Contain(() => AddType(type), type);
		}

		foreach (var type in types)
		{
			Contain(() => ScanType(type), type);
		}

		while (_constructedTypes.Count > 0 || _methodInstances.Count > 0)
		{
			if (_methodInstances.Count > 0)
			{
				var instance = _methodInstances.Dequeue();
				Contain(() => ScanMethodInstance(instance), instance.Definition);
			}
			else
			{
				var type = _constructedTypes.Dequeue();
				_constructedTypeCount++;
				Contain(() => AddType(type), type);
			}
		}
	}

	// Metadata that the reader or the collector can't handle (an obfuscated method body, for example) only skips the type or method that has it.
	// The member, or for a generic instance its definition, is the origin that UNOWASM011 names for the too large instances the action finds
	// (IsWithinLimits).
	private void Contain(Action action, MemberReference member)
	{
		CancellationToken.ThrowIfCancellationRequested();
		_origin = member is GenericInstanceType instance ? CecilTypes.TryResolve(instance) : member;

		try
		{
			action();
		}
		catch (Exception e) when (e is not OutOfMemoryException)
		{
			// The instances of a generic method whose body can't be read fail as its definition does: it's reported once.
			string skipped = $"{MemberNames.Member(member)}: {e.GetType().Name}: {e.Message}";

			if (_skippedSet.Add(skipped))
			{
				_skipped.Add(skipped);
			}
		}
	}

	// The generic instances the type's base types and interfaces name, and those of the code of its methods (ScanMethod), each of which only skips
	// itself when it can't be read.
	private void ScanType(TypeDefinition type)
	{
		if (type.BaseType != null)
		{
			QueueConstructedType(type.BaseType);
		}

		foreach (var implementation in type.Interfaces)
		{
			QueueConstructedType(implementation.InterfaceType);
		}

		foreach (var method in type.Methods)
		{
			if (method.HasBody)
			{
				Contain(() => ScanMethod(method), method);
			}
		}
	}

	// The generic instances the method's code names (instruction operands), and the delegate targets it creates. The operands of generic code that
	// depend on generic parameters are kept for the instances of that code that closed code calls.
	private void ScanMethod(MethodDefinition method)
	{
		List<Operand>? genericOperands = method.DeclaringType.HasGenericParameters || method.HasGenericParameters ? new() : null;

		foreach (var instruction in method.Body.Instructions)
		{
			var operand = new Operand(instruction.Operand, instruction.OpCode.Code is Code.Ldftn or Code.Ldvirtftn);

			if (IsOpen(operand.Value))
			{
				genericOperands?.Add(operand);
			}
			else
			{
				AddOperand(operand, null, null);
			}
		}

		if (genericOperands != null)
		{
			_genericOperands[method] = genericOperands;
		}

		FreeBody(method);

		if (IsGenericVirtualImplementation(method))
		{
			AddGenericVirtualImplementation(method);
		}
	}

	/// <summary>
	/// The code of the implementations of generic virtual methods (overrides and interface implementations) and of generic static abstract members
	/// creates generic instances too, but a call names the method it overrides or implements, and the runtime picks the implementation by the
	/// object, or the type argument, that the call is made on. So every implementation is followed with the generic arguments of every call to a
	/// generic virtual method of the same name, number of generic parameters and number of parameters (<see cref="GetGenericVirtualKey"/>), whichever
	/// the collector finds first. That over-approximates the pairs, which only costs instances that nothing runs. Implementations in generic classes
	/// and structs, and in the classes and structs nested in them, which have the generic parameters of the outer type in metadata, aren't followed:
	/// each of their instances would pair with every call, which multiplies the instances of generic code such as LINQ's iterators (a large app
	/// reached the bound on the instances of one method six times, and its generation allocated 2.8 times as much). The pairs are keyed by name, so
	/// an implementation whose name differs from the method it implements (VB's Implements, or an IL .override) isn't followed either; C# keeps the
	/// name, or prefixes it with the interface.
	/// </summary>
	private static bool IsGenericVirtualImplementation(MethodDefinition method)
		=> method.HasGenericParameters && method.HasBody && !method.DeclaringType.HasGenericParameters
			&& (method.IsVirtual || (method.IsStatic && method.HasOverrides));

	// The name without the interface of an explicit implementation (Namespace.IVisitor.Visit), the number of generic parameters and the number of
	// parameters.
	private static string GetGenericVirtualKey(MethodDefinition method)
	{
		int dot = method.Name.LastIndexOf('.');
		string name = dot < 0 ? method.Name : method.Name.Substring(dot + 1);
		return string.Format(CultureInfo.InvariantCulture, "{0}`{1}({2})", name, method.GenericParameters.Count, method.Parameters.Count);
	}

	private void AddGenericVirtualImplementation(MethodDefinition method)
	{
		string key = GetGenericVirtualKey(method);

		if (!_genericVirtualImplementations.TryGetValue(key, out var implementations))
		{
			_genericVirtualImplementations.Add(key, implementations = []);
		}

		implementations.Add(method);

		// Queueing an instance never adds a call or an implementation, so the lists don't change while they're read.
		foreach (var call in _genericVirtualCalls.TryGetValue(key, out var calls) ? calls : [])
		{
			QueueGenericVirtualImplementation(method, call);
		}
	}

	private void AddGenericVirtualCall(MethodDefinition method, GenericInstanceMethod methodContext)
	{
		string key = GetGenericVirtualKey(method);

		if (!_genericVirtualCallKeys.Add((key, string.Join(",", methodContext.GenericArguments.Select(CecilTypes.GetSharedArgumentIdentity)))))
		{
			return;
		}

		if (!_genericVirtualCalls.TryGetValue(key, out var calls))
		{
			_genericVirtualCalls.Add(key, calls = []);
		}

		calls.Add(methodContext);

		foreach (var implementation in _genericVirtualImplementations.TryGetValue(key, out var implementations) ? implementations : [])
		{
			QueueGenericVirtualImplementation(implementation, methodContext);
		}
	}

	private void QueueGenericVirtualImplementation(MethodDefinition implementation, GenericInstanceMethod call)
	{
		var methodContext = new GenericInstanceMethod(implementation);

		foreach (var argument in call.GenericArguments)
		{
			methodContext.GenericArguments.Add(argument);
		}

		QueueMethodInstance(new MethodInstance(implementation, null, methodContext));
	}

	// The bodies of all the AOT inputs take a lot of memory, and nothing reads them again (GetGenericOperands reads those it needs again).
	private static void FreeBody(MethodDefinition method) => method.Body = null;

	// Whether an operand depends on generic parameters. The members of closed generic instances don't, even though the signatures of their
	// references name the generic parameters of their declaring type.
	private static bool IsOpen(object? operand) => operand switch
	{
		TypeReference type => type.ContainsGenericParameter,
		GenericInstanceMethod method => method.DeclaringType.ContainsGenericParameter || method.GenericArguments.Any(a => a.ContainsGenericParameter),
		MemberReference member => member.DeclaringType?.ContainsGenericParameter ?? false,
		_ => false,
	};

	// What an operand constructs, calls and creates delegates to, with the generic arguments of the code that has it when it's open.
	private void AddOperand(Operand operand, GenericInstanceType? typeContext, GenericInstanceMethod? methodContext)
	{
		switch (operand.Value)
		{
			case TypeReference type:
				QueueConstructedType(CecilTypes.Inflate(type, typeContext, methodContext));
				break;

			case MethodReference method:
				var (instanceTypeContext, instanceMethodContext) = GetMethodContexts(method, typeContext, methodContext);
				QueueConstructedType(instanceTypeContext ?? CecilTypes.Inflate(method.DeclaringType, typeContext, methodContext));

				foreach (var argument in instanceMethodContext?.GenericArguments ?? [])
				{
					QueueConstructedType(argument);
				}

				// Only generic code and delegate targets need the definition, which is costly to resolve.
				if ((instanceTypeContext != null || instanceMethodContext != null || operand.IsDelegateTarget) && Resolve(method) is { } definition)
				{
					var instance = new MethodInstance(definition, instanceTypeContext, instanceMethodContext);
					QueueMethodInstance(instance);

					// Generic virtual methods aren't in IMT slots, but code compiled for any value type calls them with a signature that has
					// generic parameters, as it calls the virtual methods of generic instances (AddGenericInstanceMethods).
					if (instanceMethodContext != null && definition is { IsVirtual: true, IsStatic: false })
					{
						_virtualMethodCount++;
						AddMethod(definition, instanceTypeContext, instanceMethodContext);
					}

					if (instanceMethodContext != null && definition.IsVirtual && !instanceMethodContext.GenericArguments.Any(a => a.ContainsGenericParameter))
					{
						AddGenericVirtualCall(definition, instanceMethodContext);
					}

					if (operand.IsDelegateTarget)
					{
						AddDelegateTarget(instance);
					}
				}

				break;

			case FieldReference field:
				QueueConstructedType(CecilTypes.Inflate(field.DeclaringType, typeContext, methodContext));
				break;
		}
	}

	// The generic arguments of the declaring type of the method a reference calls and its own, given those of the code that has it.
	private static (GenericInstanceType? TypeContext, GenericInstanceMethod? MethodContext) GetMethodContexts(
		MethodReference method, GenericInstanceType? typeContext, GenericInstanceMethod? methodContext)
	{
		var declaringType = CecilTypes.StripModifiers(CecilTypes.Inflate(method.DeclaringType, typeContext, methodContext)) as GenericInstanceType;
		GenericInstanceMethod? methodInstance = null;

		if (method is GenericInstanceMethod genericMethod)
		{
			methodInstance = new GenericInstanceMethod(genericMethod.ElementMethod);

			foreach (var argument in genericMethod.GenericArguments)
			{
				methodInstance.GenericArguments.Add(CecilTypes.Inflate(argument, typeContext, methodContext));
			}
		}

		return (declaringType, methodInstance);
	}

	// By element method: the instances of a generic method resolve to its definition.
	private MethodDefinition? Resolve(MethodReference method)
	{
		var element = method.GetElementMethod();

		if (!_resolvedMethods.TryGetValue(element, out var definition))
		{
			definition = CecilTypes.TryResolve(element);
			_resolvedMethods.Add(element, definition);
		}

		return definition;
	}

	// An instance of generic code that the runtime may run: the generic instances its body names are constructed too.
	private void QueueMethodInstance(MethodInstance instance)
	{
		var definition = instance.Definition;

		if (!definition.HasBody || (instance.TypeContext == null && instance.MethodContext == null))
		{
			return;
		}

		if (HasNothingToFollow(definition))
		{
			return;
		}

		var arguments = (instance.TypeContext?.GenericArguments ?? []).Concat(instance.MethodContext?.GenericArguments ?? []).ToList();

		// The arguments of recursive generic code can nest ever deeper: they stop where the AOT compiler stops.
		if (arguments.Any(a => a.ContainsGenericParameter || GetGenericNesting(a) > MaxGenericNesting))
		{
			return;
		}

		if (_calledMethodInstances.Add((definition, string.Join(",", arguments.Select(CecilTypes.GetSharedArgumentIdentity))))
			&& IsWithinLimits(definition, arguments.Sum(GetSharedArgumentSize)))
		{
			_methodInstances.Enqueue(instance);
		}
	}

	// Following an instance reads the operands of its code that depend on generic parameters, and lists it for UNOWASM010 when it has a flagged
	// parameter of a generic parameter's type: the many instances of code that does neither (Unsafe.As<T>, for example) aren't recorded. A method
	// whose signature or body can't be read is followed anyway, so that the scan of its instance (ScanMethodInstance) skips it alone, rather than the
	// code that calls it.
	private bool HasNothingToFollow(MethodDefinition method)
	{
		try
		{
			return !HasFlaggedGenericParameter(method) && GetGenericOperands(method).Count == 0;
		}
		catch (Exception e) when (e is not OutOfMemoryException)
		{
			return false;
		}
	}

	// Whether to follow a new instance of a generic type or method definition (MaxGenericSize, MaxInstancesPerDefinition and MaxFollowedInstances),
	// and reports the limit once per cause. Too large instances are reported by the code that uses them, rather than by each generic type, method
	// and argument that a large instance has, or by each instance that recursive code makes larger.
	private bool IsWithinLimits(MemberReference definition, int size)
	{
		if (size > MaxGenericSize)
		{
			var origin = _origin ?? definition;

			if (_limitedOrigins.Add(origin))
			{
				_limits.Add($"the generic instances that {Describe(origin)} uses whose generic arguments hold more than {MaxGenericSize} types");
			}

			return false;
		}

		_instanceCounts.TryGetValue(definition, out int count);

		if (count >= MaxInstancesPerDefinition)
		{
			if (_limitedDefinitions.Add(definition))
			{
				_limits.Add($"the generic instances of {Describe(definition)} past the first {Format(MaxInstancesPerDefinition)}");
			}

			return false;
		}

		if (_followedCount >= MaxFollowedInstances)
		{
			if (!_isFollowedCountReported)
			{
				_isFollowedCountReported = true;
				var most = _instanceCounts.OrderByDescending(c => c.Value).First();
				_limits.Add($"the generic instances past the first {Format(MaxFollowedInstances)}, {Format(most.Value)} of which are of {Describe(most.Key)}");
			}

			return false;
		}

		_instanceCounts[definition] = count + 1;
		_followedCount++;
		return true;
	}

	private static string Format(int count) => count.ToString("N0", CultureInfo.InvariantCulture);

	private static string Describe(MemberReference definition) => MemberNames.Member(definition);

	// Whether a parameter of the method has flags and a generic parameter's type (UnenterableMethods). It runs for every call to generic code, so it
	// doesn't allocate.
	private static bool HasFlaggedGenericParameter(MethodDefinition method)
	{
		var parameters = method.Parameters;

		for (int i = 0; i < parameters.Count; i++)
		{
			if (parameters[i].Attributes != ParameterAttributes.None && CecilTypes.StripModifiers(parameters[i].ParameterType) is GenericParameter)
			{
				return true;
			}
		}

		return false;
	}

	private void ScanMethodInstance(MethodInstance instance)
	{
		if (HasFlaggedGenericParameter(instance.Definition))
		{
			_flaggedMethodInstances.Add(instance);
		}

		foreach (var operand in GetGenericOperands(instance.Definition))
		{
			AddOperand(operand, instance.TypeContext, instance.MethodContext);
		}
	}

	// The operands of generic code that depend on generic parameters, read here when the collector hasn't scanned the code's module yet.
	private List<Operand> GetGenericOperands(MethodDefinition method)
	{
		if (!_genericOperands.TryGetValue(method, out var operands))
		{
			operands = method.Body.Instructions
				.Select(i => new Operand(i.Operand, i.OpCode.Code is Code.Ldftn or Code.Ldvirtftn))
				.Where(o => IsOpen(o.Value))
				.ToList();

			_genericOperands.Add(method, operands);
			FreeBody(method);
		}

		return operands;
	}

	private void AddDelegateTarget(MethodInstance target)
	{
		var definition = target.Definition;

		if ((definition.DeclaringType.HasGenericParameters && target.TypeContext == null) || (definition.HasGenericParameters && target.MethodContext == null))
		{
			return;
		}

		_delegateTargetCount++;
		AddMethod(definition, target.TypeContext, target.MethodContext);
	}

	private void AddType(TypeReference type)
	{
		if (!_visitedTypes.Add(CecilTypes.GetIdentity(type)))
		{
			return;
		}

		var definition = CecilTypes.TryResolve(type);

		if (definition == null)
		{
			return;
		}

		// An interface instance that a call site names: the class that implements it may be created where the collector can't see it.
		if (definition.IsInterface)
		{
			AddInterface(type);
		}
		else
		{
			AddGenericInstanceMethods(type);
		}

		foreach (var iface in GetAllInterfaces(type))
		{
			AddInterface(iface);
		}
	}

	// The generic instances among the type and its base types: their virtual methods that are declared with their generic parameters are seeded,
	// and the methods the runtime reaches without a call that names them are followed. Delegates enter the interpreter through their targets
	// (AddDelegateTarget), not through Invoke, BeginInvoke or EndInvoke.
	private void AddGenericInstanceMethods(TypeReference type)
	{
		TypeReference? current = CecilTypes.StripModifiers(type);

		for (int depth = 0; current != null && depth < MaxBaseTypes; depth++)
		{
			var definition = CecilTypes.TryResolve(current);

			if (definition == null || CecilTypes.IsDelegate(definition))
			{
				return;
			}

			if (current is GenericInstanceType instance && _visitedGenericInstances.Add(CecilTypes.GetIdentity(instance)))
			{
				foreach (var method in definition.Methods)
				{
					if (method.IsVirtual && !method.IsStatic && HasGenericParameterInSignature(method))
					{
						_virtualMethodCount++;
						AddMethod(method, instance, null);
					}

					if (IsReachedIndirectly(method))
					{
						QueueMethodInstance(new MethodInstance(method, instance, null));
					}

				}
			}

			current = definition.BaseType == null ? null : CecilTypes.Inflate(definition.BaseType, current as GenericInstanceType, null);
		}
	}

	private static bool HasGenericParameterInSignature(MethodDefinition method)
		=> method.ReturnType.ContainsGenericParameter || method.Parameters.Any(p => p.ParameterType.ContainsGenericParameter);

	/// <summary>
	/// Returns whether the runtime reaches a method of a generic instance without a call that names the instance: virtual methods through vtables
	/// and IMT slots (overrides, interface implementations and default implementations, the MoveNext methods of async methods and iterators among
	/// them), static constructors when the type is initialized (static field initializers), parameterless constructors, which <c>new T()</c> runs
	/// through reflection (<c>Activator.CreateInstance&lt;T&gt;</c>, instance field initializers included), and the static methods that implement
	/// static abstract members, which constrained calls reach through the interface method. Generic methods need a call for their arguments.
	/// </summary>
	private static bool IsReachedIndirectly(MethodDefinition method)
		=> !method.HasGenericParameters
			&& (method.IsVirtual || (method.IsConstructor && (method.IsStatic || !method.HasParameters)) || (method.IsStatic && method.HasOverrides));

	private void AddInterface(TypeReference iface)
	{
		if (!_visitedInterfaces.Add(CecilTypes.GetIdentity(iface)))
		{
			return;
		}

		var definition = CecilTypes.TryResolve(iface);

		if (definition == null)
		{
			return;
		}

		// build_imt_slots in object.c only puts virtual instance methods in IMT slots.
		foreach (var method in definition.Methods)
		{
			if (method.IsVirtual && !method.IsStatic)
			{
				_interfaceMethodCount++;
				AddMethod(method, iface as GenericInstanceType, null);
			}

			if (iface is GenericInstanceType instance && IsReachedIndirectly(method))
			{
				QueueMethodInstance(new MethodInstance(method, instance, null));
			}
		}
	}

	// The interfaces the type implements, directly, through its base types or through other interfaces, inflated for generic instances.
	private static IEnumerable<TypeReference> GetAllInterfaces(TypeReference type)
	{
		var interfaces = new Dictionary<string, TypeReference>(StringComparer.Ordinal);

		void Visit(TypeReference current, int depth)
		{
			var definition = CecilTypes.TryResolve(current);

			if (definition == null || depth >= MaxBaseTypes)
			{
				return;
			}

			var context = current as GenericInstanceType;

			foreach (var implementation in definition.Interfaces)
			{
				var iface = CecilTypes.Inflate(implementation.InterfaceType, context, null);
				string identity = CecilTypes.GetIdentity(iface);

				if (!interfaces.ContainsKey(identity))
				{
					interfaces.Add(identity, iface);
					Visit(iface, depth + 1);
				}
			}

			if (definition.BaseType != null)
			{
				Visit(CecilTypes.Inflate(definition.BaseType, context, null), depth + 1);
			}
		}

		Visit(type, 0);
		return interfaces.Values;
	}

	private void AddMethod(MethodDefinition method, GenericInstanceType? typeContext, GenericInstanceMethod? methodContext)
	{
		if (method.HasGenericParameters && methodContext == null)
		{
			return;
		}

		var returnType = CecilTypes.Inflate(method.ReturnType, typeContext, methodContext);
		var parameterTypes = method.Parameters.Select(p => CecilTypes.Inflate(p.ParameterType, typeContext, methodContext)).ToList();
		var seed = SeedSignature.From(!method.IsStatic, returnType, parameterTypes, new SeedOrigin(method, typeContext));

		if (seed == null)
		{
			_unsupportedCount++;
			return;
		}

		if (!_seeds.ContainsKey(seed.Key))
		{
			_seeds.Add(seed.Key, seed);
		}

		// Generic types that the signature names: the code creates their instances, and their interface methods are resolved through IMT slots too.
		QueueConstructedType(seed.ReturnType);

		foreach (var parameterType in seed.ParameterTypes)
		{
			QueueConstructedType(parameterType);
		}
	}

	private void QueueConstructedType(TypeReference type)
	{
		type = CecilTypes.StripModifiers(type);

		while (type is TypeSpecification specification and not GenericInstanceType)
		{
			type = CecilTypes.StripModifiers(specification.ElementType);
		}

		if (type is not GenericInstanceType instance || instance.ContainsGenericParameter)
		{
			return;
		}

		string identity = CecilTypes.GetIdentity(instance);

		if (!_queuedTypes.Add(identity))
		{
			return;
		}

		// Signatures can name ever deeper instances (a method of C<T> that returns C<ValueTuple<T>>): they stop where the AOT compiler stops. The
		// runtime enters the interpreter for the methods of instances over reference types too (their code is shared, their signatures normalize
		// the references), and instances that only differ in reference type arguments have the same signatures, so one of them is enough.
		if (GetGenericNesting(instance) <= MaxGenericNesting && !_visitedTypes.Contains(identity) && CecilTypes.TryResolve(instance) is { } definition
			&& instance.GenericArguments.All(CecilTypes.CanResolve) && _sharedTypes.Add(CecilTypes.GetSharedIdentity(instance))
			&& IsWithinLimits(definition, GetSharedGenericSize(instance)))
		{
			_constructedTypes.Enqueue(instance);
		}

		foreach (var argument in instance.GenericArguments)
		{
			QueueConstructedType(argument);
		}
	}

	/// <summary>
	/// Returns how many generic instances are nested in each other: 2 for <c>C&lt;D&lt;int&gt;&gt;</c>.
	/// </summary>
	internal static int GetGenericNesting(TypeReference type)
		=> CecilTypes.StripModifiers(type) is GenericInstanceType instance ? 1 + instance.GenericArguments.Max(GetGenericNesting) : 0;

	/// <summary>
	/// Returns how many named types a type holds, its generic arguments and theirs included, counting a reference type argument as one, as
	/// <see cref="CecilTypes.GetSharedIdentity"/> does: the runtime shares the code of instances that only differ in reference type arguments, so the
	/// limits only depend on what they share. 3 for <c>C&lt;D&lt;int&gt;&gt;</c> when D is a struct, 2 when it's a class, 1 for <c>int[]</c>.
	/// </summary>
	internal static int GetSharedGenericSize(TypeReference type) => CecilTypes.StripModifiers(type) switch
	{
		GenericInstanceType instance => 1 + instance.GenericArguments.Sum(GetSharedArgumentSize),
		TypeSpecification specification and not FunctionPointerType => GetSharedGenericSize(specification.ElementType),
		_ => 1,
	};

	// The size of a generic argument as the runtime shares it (CecilTypes.GetSharedArgumentIdentity).
	private static int GetSharedArgumentSize(TypeReference argument) => CecilTypes.IsValueType(argument) ? GetSharedGenericSize(argument) : 1;

	/// <summary>
	/// A method, with the generic arguments of its declaring type and its own when it has them.
	/// </summary>
	public readonly record struct MethodInstance(MethodDefinition Definition, GenericInstanceType? TypeContext, GenericInstanceMethod? MethodContext);

	// An instruction operand, and whether the instruction loads it as a delegate target (ldftn, ldvirtftn).
	private readonly record struct Operand(object? Value, bool IsDelegateTarget);
}
