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
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace Uno.Wasm.Bootstrap.UnitTests;

// The code the seeding tests of Given_AOTWrapperSeeding generate the seeds of.
public interface IAotSeedingSample
{
	long Distance(long a, long b);

	DateTime GetTime();

	string GetRecords(string filter, int skip, int take, bool ascending, long from, long to, double minScore, double maxScore, int flags);

	void Store(AotSeeding\u00C9tat value);

	void Store(AotSeeding\u00C9tat value, int a, int b, int c, int d, int e, int f, int g, int h);

	void Fill(AotSeedingInlineArray values, sbyte a, sbyte b, sbyte c);

	void Put(AotSeedingBox<AotSeedingPacked> box);
}

// A type with a non-ASCII name (E with an acute accent). With explicit layout, wrapper names keep the type (get_wrapper_shared_vtype only maps
// auto and sequential layouts).
[StructLayout(LayoutKind.Explicit)]
public struct AotSeeding\u00C9tat
{
	[FieldOffset(0)]
	public int Value;
}

[InlineArray(2)]
public struct AotSeedingInlineArray
{
	public int Element;
}

// 5 bytes aligned to 1, so get_wrapper_shared_vtype maps it to its fields, Mono.ValueTuple`2<byte, int>.
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct AotSeedingPacked
{
	public byte Tag;
	public int Value;
}

public struct AotSeedingBox<T>
{
	public T Value;
}

[StructLayout(LayoutKind.Sequential, Pack = 8)]
public struct AotSeedingPackEight
{
	public int A;
	public int B;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct AotSeedingPackOneReference
{
	public byte A;
	public string B;
}

[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct AotSeedingPackFourBytes
{
	public byte A;
	public byte B;
}

[StructLayout(LayoutKind.Sequential, Pack = 4, Size = 6)]
public struct AotSeedingPackSized
{
	public int A;
}

// Each instance has a method that returns a deeper one.
public class AotSeedingRecursive<T>
{
	public virtual AotSeedingRecursive<ValueTuple<T>> Wrap() => new();

	public virtual void Take(AotSeedingRecursive<T> owner, T value, sbyte a, sbyte b, sbyte c, sbyte d)
	{
	}
}

public interface IAotSeedingGenericService<T>
{
	void Process(T a, double b, T c, double d, T e, double f, T g, double h, T i);
}

public struct AotSeedingKey
{
	public long Id;
	public double Weight;
}

public interface IAotSeedingLookup<T>
{
	int Count();

	T Find(T a, T b, int c, int d, int e, int f, int g, int h, int i);
}

public static class AotSeedingLookups
{
	// The class that implements the interface is only known at run time.
	public static int Count(object lookup) => ((IAotSeedingLookup<AotSeedingKey>)lookup).Count();
}

public interface IAotSeedingRepository<T>
{
	int Count();
}

public interface IAotSeedingFinder<T>
{
	T Find(int a, T b, int c, int d, int e, int f, int g, int h, int i);
}

public sealed class AotSeedingRepository<T> : IAotSeedingRepository<T>, IAotSeedingFinder<T>
{
	public int Count() => 0;

	public T Find(int a, T b, int c, int d, int e, int f, int g, int h, int i) => b;
}

public static class AotSeedingRepositoryFactory
{
	public static IAotSeedingRepository<T> Create<T>() => new AotSeedingRepository<T>();

	public static int Use() => Create<AotSeedingKey>().Count();
}

public enum AotSeedingByteKind : byte
{
	None,
}

public enum AotSeedingUShortKind : ushort
{
	None,
}

public unsafe interface IAotSeedingFunctionPointers
{
	void Call(delegate*<int, void> callback);

	void Call(delegate*<long, void> callback);

	void Call(delegate*<long, void> callback, decimal a, decimal b, decimal c, decimal d, decimal e, decimal f, decimal g, decimal h);
}

public interface IAotSeedingUnsupported
{
	void Take(TypedReference value);

	void Take(ArgIterator value);

	void Take(RuntimeArgumentHandle value);

	void Take<T>(T value, decimal a);
}

public interface IAotSeedingPairs
{
	void Take(KeyValuePair<string, int> pair, decimal a, decimal b, decimal c);

	void Take(KeyValuePair<object, int> pair, decimal a, decimal b, decimal c);
}

public interface IAotSeedingMergeable
{
	void Take(bool a, decimal b, decimal c);

	void Take(byte a, decimal b, decimal c);

	void Take(AotSeedingByteKind a, decimal b, decimal c);

	void Take(char a, decimal b, decimal c);

	void Take(ushort a, decimal b, decimal c);

	void Take(AotSeedingUShortKind a, decimal b, decimal c);
}

public abstract class AotSeedingComparer<T>
{
	public abstract int Compare(T a, T b, short c, short d, short e, short f, short g, short h, short i);
}

public sealed class AotSeedingKeyComparer : AotSeedingComparer<AotSeedingKey>
{
	public override int Compare(AotSeedingKey a, AotSeedingKey b, short c, short d, short e, short f, short g, short h, short i) => 0;
}

public class AotSeedingVirtualSample
{
	public virtual string Describe(decimal a, decimal b, decimal c, decimal d, decimal e, decimal f, decimal g, decimal h, Guid i) => "";
}

public delegate void AotSeedingCallback(decimal a, decimal b, decimal c, decimal d, decimal e, decimal f, decimal g, decimal h, decimal i);

public interface IAotSeedingWithHelper
{
	string Name { get; }

	private string Format(Guid a, Guid b, Guid c, Guid d, Guid e, Guid f, Guid g, Guid h, Guid i) => Name;
}

public static class AotSeedingGenericDelegates
{
	public static Action<int, int, T, int, int, int, int, int, int> Create<T>() => Handle<T>;

	public static void Handle<T>(int a, int b, T c, int d, int e, int f, int g, int h, int i)
	{
	}

	public static object Use() => Create<AotSeedingKey>();
}

public sealed class AotSeedingGenericService<T> : IAotSeedingGenericService<T>
{
	public void Process(T a, double b, T c, double d, T e, double f, T g, double h, T i)
	{
	}
}

public static class AotSeedingSamples
{
	public static long Add(long a, long b) => a + b;

	public static object CreateRecursive() => new AotSeedingRecursive<int>();

	public static object CreateGenericService() => new AotSeedingGenericService<string>();

	public static Func<decimal, Guid, TimeSpan, ushort, sbyte, short> CreateDelegate() => (a, b, c, d, e) => 0;
}

public sealed class AotSeedingSample : IAotSeedingSample
{
	public long Distance(long a, long b) => Math.Abs(b - a);

	public DateTime GetTime() => DateTime.UnixEpoch;

	public string GetRecords(string filter, int skip, int take, bool ascending, long from, long to, double minScore, double maxScore, int flags)
		=> filter;

	public void Store(AotSeeding\u00C9tat value)
	{
	}

	public void Store(AotSeeding\u00C9tat value, int a, int b, int c, int d, int e, int f, int g, int h)
	{
	}

	public void Fill(AotSeedingInlineArray values, sbyte a, sbyte b, sbyte c)
	{
	}

	public void Put(AotSeedingBox<AotSeedingPacked> box)
	{
	}

	public Func<long, long, long> CreateAddDelegate() => AotSeedingSamples.Add;
}

public interface IAotSeedingStore<T>
{
	int Count();

	T Find(T a, int b, int c, int d, int e, int f, int g, int h, int i);
}

public interface IAotSeedingCounter
{
	int Count();
}

public sealed class AotSeedingStore<T> : IAotSeedingStore<T>, IAotSeedingCounter
{
	public int Count() => 0;

	public T Find(T a, int b, int c, int d, int e, int f, int g, int h, int i) => a;
}

// With explicit layout, so that the wrapper names keep each key.
[StructLayout(LayoutKind.Explicit)] public struct AotSeedingDirectKey { [FieldOffset(0)] public int Value; }
[StructLayout(LayoutKind.Explicit)] public struct AotSeedingActivatorKey { [FieldOffset(0)] public int Value; }
[StructLayout(LayoutKind.Explicit)] public struct AotSeedingRecordKey { [FieldOffset(0)] public int Value; }
[StructLayout(LayoutKind.Explicit)] public struct AotSeedingDefaultKey { [FieldOffset(0)] public int Value; }
[StructLayout(LayoutKind.Explicit)] public struct AotSeedingAsyncKey { [FieldOffset(0)] public int Value; }
[StructLayout(LayoutKind.Explicit)] public struct AotSeedingIteratorKey { [FieldOffset(0)] public int Value; }
[StructLayout(LayoutKind.Explicit)] public struct AotSeedingOverrideKey { [FieldOffset(0)] public int Value; }
[StructLayout(LayoutKind.Explicit)] public struct AotSeedingImplementationKey { [FieldOffset(0)] public int Value; }
[StructLayout(LayoutKind.Explicit)] public struct AotSeedingInitializerKey { [FieldOffset(0)] public int Value; }
[StructLayout(LayoutKind.Explicit)] public struct AotSeedingVisitKey { [FieldOffset(0)] public int Value; }
[StructLayout(LayoutKind.Explicit)] public struct AotSeedingDefaultsKey { [FieldOffset(0)] public int Value; }

public static class AotSeedingStores
{
	public static int Count<T>() => ((IAotSeedingStore<T>)new AotSeedingStore<T>()).Count();

	public static T Create<T>() where T : new() => new T();

	public static IAotSeedingCounter CreateWithDefault<T>() => new AotSeedingDefaultStore<T>();

	public static async Task<int> CountAsync<T>()
	{
		await Task.Yield();
		IAotSeedingStore<T> store = new AotSeedingStore<T>();
		return store.Count();
	}

	public static IEnumerable<int> Counts<T>()
	{
		IAotSeedingStore<T> store = new AotSeedingStore<T>();
		yield return store.Count();
	}

	public static object Use() =>
	(
		Count<AotSeedingDirectKey>(),
		((IAotSeedingCounter)Create<AotSeedingStore<AotSeedingActivatorKey>>()).Count(),
		CreateWithDefault<AotSeedingDefaultKey>().Count(),
		CountAsync<AotSeedingAsyncKey>(),
		Counts<AotSeedingIteratorKey>().First(),
		((AotSeedingCounterBase)new AotSeedingOverride<AotSeedingOverrideKey>()).Count(),
		((IAotSeedingCounter)new AotSeedingImplementation<AotSeedingImplementationKey>()).Count(),
		AotSeedingSingleton<AotSeedingInitializerKey>.Instance.Count()
	);
}

public record struct AotSeedingRecord(int X) : IAotSeedingStore<AotSeedingRecordKey>
{
	public int Count() => X;

	public AotSeedingRecordKey Find(AotSeedingRecordKey a, int b, int c, int d, int e, int f, int g, int h, int i) => a;
}

public interface IAotSeedingDefaultStore<T>
{
	int Count();

	T Find(T a, int b, int c, int d, int e, int f, int g, int h, int i) => a;
}

public sealed class AotSeedingDefaultStore<T> : IAotSeedingDefaultStore<T>, IAotSeedingCounter
{
	public int Count() => 0;
}

public abstract class AotSeedingCounterBase
{
	public abstract int Count();
}

public sealed class AotSeedingOverride<T> : AotSeedingCounterBase
{
	public override int Count() => ((IAotSeedingStore<T>)new AotSeedingStore<T>()).Count();
}

public sealed class AotSeedingImplementation<T> : IAotSeedingCounter
{
	public int Count() => ((IAotSeedingStore<T>)new AotSeedingStore<T>()).Count();
}

public static class AotSeedingSingleton<T>
{
	public static readonly IAotSeedingCounter Instance = new AotSeedingStore<T>();
}

public abstract class AotSeedingVisitor
{
	public abstract T Visit<T>(T a, int b, int c, int d, int e, int f, int g, int h, int i);
}

public sealed class AotSeedingVisitorImplementation : AotSeedingVisitor
{
	public override T Visit<T>(T a, int b, int c, int d, int e, int f, int g, int h, int i) => a;
}

public static class AotSeedingVisits
{
	public static T Relay<T>(AotSeedingVisitor visitor, T value) => visitor.Visit(value, 0, 0, 0, 0, 0, 0, 0, 0);

	public static AotSeedingVisitKey Use() => Relay(new AotSeedingVisitorImplementation(), default(AotSeedingVisitKey));
}

public sealed class AotSeedingGenericDefaults<T>
{
	public string Describe(int a, int b, int c, int d, int e, int f, int g, int h, T value = default!) => "";

	public string DescribeFew(int a, T value = default!) => "";

	// A struct parameter and a generic one with flags: one warning.
	public string DescribeBoth(AotSeedingExplicitBox box = default, T value = default!) => "";

	public static string Use() => new AotSeedingGenericDefaults<AotSeedingDefaultsKey>().Describe(0, 0, 0, 0, 0, 0, 0, 0)
		+ new AotSeedingGenericDefaults<AotSeedingDefaultsKey>().DescribeFew(0) + new AotSeedingGenericDefaults<AotSeedingDefaultsKey>().DescribeBoth();
}

public static class AotSeedingGenericMethodDefaults
{
	public static string Describe<T>(int a, int b, int c, int d, int e, int f, int g, int h, T value = default!) => "";

	public static string DescribeFew<T>(int a, T value = default!) => "";

	public static string Use() => Describe<AotSeedingDefaultsKey>(0, 0, 0, 0, 0, 0, 0, 0) + DescribeFew<AotSeedingDefaultsKey>(0);
}

// Generic code that the runtime reaches in other ways than a call that closed code makes, each of which creates an AotSeedingStore<T> over a key of
// its own (AotSeedingReachedStores.Use).
public static class AotSeedingReachedStores
{
	public static T Create<T>() where T : new() => new T();

	public static IAotSeedingCounter CreateThroughInterface<T>() where T : IAotSeedingCreatable => T.Create();

	public static IAotSeedingCounter CreateByDefault<T>() where T : IAotSeedingStaticFactory<AotSeedingStaticDefaultKey> => T.Create();

	public static Func<Task<int>> CountLater<T>() => async () =>
	{
		await Task.Yield();
		return ((IAotSeedingStore<T>)new AotSeedingStore<T>()).Count();
	};

	public static async IAsyncEnumerable<int> CountAllAsync<T>()
	{
		await Task.Yield();
		yield return ((IAotSeedingStore<T>)new AotSeedingStore<T>()).Count();
	}

	public static Func<int> Capture<T>(int x) => () => ((IAotSeedingStore<T>)new AotSeedingStore<T>()).Count() + x;

	public static Func<int> CountStatically<T>() => static () => ((IAotSeedingStore<T>)new AotSeedingStore<T>()).Count();

	public static int CountInLocalFunction<T>()
	{
		return Count();

		static int Count() => ((IAotSeedingStore<T>)new AotSeedingStore<T>()).Count();
	}

	public static int CountInCapturingLocalFunction<T>(int x)
	{
		return Count();

		int Count() => ((IAotSeedingStore<T>)new AotSeedingStore<T>()).Count() + x;
	}

	public static Lazy<IAotSeedingCounter> CreateLazily<T>() => new(() => new AotSeedingStore<T>());

	public static int Consume(Func<IAotSeedingCounter> factory) => factory().Count();

	public static int CountThroughFactory<T>() => Consume(() => new AotSeedingStore<T>());

	public static int CountAll<T>(int n) => Enumerable.Range(0, n).Select(_ => (IAotSeedingStore<T>)new AotSeedingStore<T>()).Sum(s => s.Count());

	public static int CountConstrained<TCounter>(TCounter counter) where TCounter : IAotSeedingCounter => counter.Count();

	public static object[] Use() =>
	[
		Create<AotSeedingConstructed<AotSeedingConstructorKey>>().Counter.Count(),
		Create<AotSeedingInitialized<AotSeedingFieldInitializerKey>>().Counter.Count(),
		new Lazy<AotSeedingConstructed<AotSeedingLazyKey>>().Value.Counter.Count(),
		CreateThroughInterface<AotSeedingCreatable<AotSeedingStaticAbstractKey>>().Count(),
		new AotSeedingAsyncHost<AotSeedingAsyncHostKey>().CountAsync(),
		CountLater<AotSeedingAsyncLambdaKey>()(),
		CountAllAsync<AotSeedingAsyncIteratorKey>().GetAsyncEnumerator(),
		((AotSeedingCounterBase)new AotSeedingDerivedOverride<AotSeedingBaseOverrideKey>()).Count(),
		((IAotSeedingCounter)new AotSeedingExplicitImplementation<AotSeedingExplicitKey>()).Count(),
		((IAotSeedingCounter)new AotSeedingClosedImplementation()).Count(),
		Capture<AotSeedingClosureKey>(1)(),
		CountStatically<AotSeedingStaticLambdaKey>()(),
		CountInLocalFunction<AotSeedingLocalFunctionKey>(),
		CountInCapturingLocalFunction<AotSeedingCapturingLocalFunctionKey>(1),
		CreateLazily<AotSeedingLazyFactoryKey>().Value.Count(),
		CountThroughFactory<AotSeedingFactoryKey>(),
		CountAll<AotSeedingLinqKey>(1),
		CountConstrained(new AotSeedingStructCounter<AotSeedingConstrainedKey>()),
		CreateByDefault<AotSeedingStaticFactory>().Count(),
		new AotSeedingCountingRecord<AotSeedingRecordClassKey>(1).ToString(),
	];
}

// Only created by new T(), which runs the parameterless constructor through reflection (Activator.CreateInstance<T>).
public sealed class AotSeedingConstructed<T>
{
	public readonly IAotSeedingCounter Counter;

	public AotSeedingConstructed() => Counter = new AotSeedingStore<T>();
}

// An instance field initializer is in the constructor too.
public sealed class AotSeedingInitialized<T>
{
	public readonly IAotSeedingCounter Counter = new AotSeedingStore<T>();
}

// A static abstract member: its implementation is a static method, which a constrained call reaches through the interface method.
public interface IAotSeedingCreatable
{
	static abstract IAotSeedingCounter Create();
}

public sealed class AotSeedingCreatable<T> : IAotSeedingCreatable
{
	public static IAotSeedingCounter Create() => new AotSeedingStore<T>();
}

public interface IAotSeedingStaticFactory<TSelf>
{
	static virtual IAotSeedingCounter Create() => new AotSeedingStore<TSelf>();
}

public sealed class AotSeedingStaticFactory : IAotSeedingStaticFactory<AotSeedingStaticDefaultKey>
{
}

public sealed class AotSeedingAsyncHost<T>
{
	public async Task<int> CountAsync()
	{
		await Task.Yield();
		return ((IAotSeedingStore<T>)new AotSeedingStore<T>()).Count();
	}
}

public abstract class AotSeedingBaseOverride<T> : AotSeedingCounterBase
{
	public override int Count() => ((IAotSeedingStore<T>)new AotSeedingStore<T>()).Count();
}

public sealed class AotSeedingDerivedOverride<T> : AotSeedingBaseOverride<T>
{
}

public sealed class AotSeedingExplicitImplementation<T> : IAotSeedingCounter
{
	int IAotSeedingCounter.Count() => ((IAotSeedingStore<T>)new AotSeedingStore<T>()).Count();
}

public class AotSeedingInheritedImplementation<T> : IAotSeedingCounter
{
	public int Count() => ((IAotSeedingStore<T>)new AotSeedingStore<T>()).Count();
}

public sealed class AotSeedingClosedImplementation : AotSeedingInheritedImplementation<AotSeedingInheritedKey>
{
}

public struct AotSeedingStructCounter<T> : IAotSeedingCounter
{
	public readonly int Count() => ((IAotSeedingStore<T>)new AotSeedingStore<T>()).Count();
}

public record AotSeedingCountingRecord<T>(int X)
{
	protected virtual bool PrintMembers(System.Text.StringBuilder builder) => ((IAotSeedingStore<T>)new AotSeedingStore<T>()).Count() == X;
}

[StructLayout(LayoutKind.Explicit)] public struct AotSeedingConstructorKey { [FieldOffset(0)] public int Value; }
[StructLayout(LayoutKind.Explicit)] public struct AotSeedingFieldInitializerKey { [FieldOffset(0)] public int Value; }
[StructLayout(LayoutKind.Explicit)] public struct AotSeedingLazyKey { [FieldOffset(0)] public int Value; }
[StructLayout(LayoutKind.Explicit)] public struct AotSeedingStaticAbstractKey { [FieldOffset(0)] public int Value; }
[StructLayout(LayoutKind.Explicit)] public struct AotSeedingAsyncHostKey { [FieldOffset(0)] public int Value; }
[StructLayout(LayoutKind.Explicit)] public struct AotSeedingAsyncLambdaKey { [FieldOffset(0)] public int Value; }
[StructLayout(LayoutKind.Explicit)] public struct AotSeedingAsyncIteratorKey { [FieldOffset(0)] public int Value; }
[StructLayout(LayoutKind.Explicit)] public struct AotSeedingBaseOverrideKey { [FieldOffset(0)] public int Value; }
[StructLayout(LayoutKind.Explicit)] public struct AotSeedingExplicitKey { [FieldOffset(0)] public int Value; }
[StructLayout(LayoutKind.Explicit)] public struct AotSeedingInheritedKey { [FieldOffset(0)] public int Value; }
[StructLayout(LayoutKind.Explicit)] public struct AotSeedingClosureKey { [FieldOffset(0)] public int Value; }
[StructLayout(LayoutKind.Explicit)] public struct AotSeedingStaticLambdaKey { [FieldOffset(0)] public int Value; }
[StructLayout(LayoutKind.Explicit)] public struct AotSeedingLocalFunctionKey { [FieldOffset(0)] public int Value; }
[StructLayout(LayoutKind.Explicit)] public struct AotSeedingCapturingLocalFunctionKey { [FieldOffset(0)] public int Value; }
[StructLayout(LayoutKind.Explicit)] public struct AotSeedingLazyFactoryKey { [FieldOffset(0)] public int Value; }
[StructLayout(LayoutKind.Explicit)] public struct AotSeedingFactoryKey { [FieldOffset(0)] public int Value; }
[StructLayout(LayoutKind.Explicit)] public struct AotSeedingLinqKey { [FieldOffset(0)] public int Value; }
[StructLayout(LayoutKind.Explicit)] public struct AotSeedingConstrainedKey { [FieldOffset(0)] public int Value; }
[StructLayout(LayoutKind.Explicit)] public struct AotSeedingStaticDefaultKey { [FieldOffset(0)] public int Value; }
[StructLayout(LayoutKind.Explicit)] public struct AotSeedingRecordClassKey { [FieldOffset(0)] public int Value; }

// A class whose instance below holds more than 64 named types (SignatureCollector.MaxGenericSize).
public sealed class AotSeedingWide<T1, T2, T3, T4, T5, T6, T7, T8>
{
}

// The runtime shares the code of the instances of AotSeedingShapeHolder<T> over reference types, and the seeding follows one instance of each shape.
public class AotSeedingShapeHolder<T> : IAotSeedingCounter
{
	public virtual int Count() => ((IAotSeedingStore<KeyValuePair<T, AotSeedingShapeKey>>)new AotSeedingStore<KeyValuePair<T, AotSeedingShapeKey>>()).Count();
}

public static class AotSeedingShapes
{
	// The large instance comes first, then a small one of the same shape.
	public static int Use()
	{
		IAotSeedingCounter large = new AotSeedingShapeHolder<AotSeedingWide<
			AotSeedingWide<int, int, int, int, int, int, int, int>, AotSeedingWide<int, int, int, int, int, int, int, int>,
			AotSeedingWide<int, int, int, int, int, int, int, int>, AotSeedingWide<int, int, int, int, int, int, int, int>,
			AotSeedingWide<int, int, int, int, int, int, int, int>, AotSeedingWide<int, int, int, int, int, int, int, int>,
			AotSeedingWide<int, int, int, int, int, int, int, int>, AotSeedingWide<int, int, int, int, int, int, int, int>>>();

		IAotSeedingCounter small = new AotSeedingShapeHolder<string>();
		return large.Count() + small.Count();
	}
}

[StructLayout(LayoutKind.Explicit)] public struct AotSeedingShapeKey { [FieldOffset(0)] public int Value; }

// A struct whose instance below holds more than 64 named types (SignatureCollector.MaxGenericSize).
public struct AotSeedingWideValue<T1, T2, T3, T4, T5, T6, T7, T8>
{
}

public static class AotSeedingWideValues
{
	// The holder, its constructor and its argument are too large for the seeding: UNOWASM011 names this method once.
	public static object Use() => new AotSeedingShapeHolder<AotSeedingWideValue<
		AotSeedingWideValue<int, int, int, int, int, int, int, int>, AotSeedingWideValue<int, int, int, int, int, int, int, int>,
		AotSeedingWideValue<int, int, int, int, int, int, int, int>, AotSeedingWideValue<int, int, int, int, int, int, int, int>,
		AotSeedingWideValue<int, int, int, int, int, int, int, int>, AotSeedingWideValue<int, int, int, int, int, int, int, int>,
		AotSeedingWideValue<int, int, int, int, int, int, int, int>, AotSeedingWideValue<int, int, int, int, int, int, int, int>>>();
}

// The build messages name these types as C# does.
public class AotSeedingOuter<TOuter>
{
	public class Inner<TInner>
	{
	}
}

public static unsafe class AotSeedingNames
{
	public static void Take<T>(
		in int a, out long b, int? c, (int, string) d, AotSeedingOuter<string>.Inner<T> e, delegate*<int, void> f, List<int>[] g, T* h)
		where T : unmanaged
		=> b = 0;

	public static int Run()
	{
		return Local(1);

		static int Local(int value) => value;
	}
}

// A generic interface with a function pointer over its type parameter: the closed instance that a call site names has a closed function pointer,
// delegate*<AotSeedingCallbackKey, void>, in its signature.
public unsafe interface IAotSeedingCallbacks<T>
{
	void Take(delegate*<T, void> callback, long b, long c, long d, long e, long f, long g, long h, long i);
}

public sealed unsafe class AotSeedingCallbacks<T> : IAotSeedingCallbacks<T>
{
	public void Take(delegate*<T, void> callback, long b, long c, long d, long e, long f, long g, long h, long i)
	{
	}
}

[StructLayout(LayoutKind.Explicit)] public struct AotSeedingCallbackKey { [FieldOffset(0)] public int Value; }

public static unsafe class AotSeedingCallbackUse
{
	public static void Use()
		=> ((IAotSeedingCallbacks<AotSeedingCallbackKey>)new AotSeedingCallbacks<AotSeedingCallbackKey>()).Take(null, 0, 0, 0, 0, 0, 0, 0, 0);
}

// A struct that nests another the given number of times: the layout walks go one level deeper for each.
public struct AotSeedingDepth<T>
{
	public T Value;
}

// Legal structs that hold a struct over themselves, through a reference: the runtime's mapping of their type arguments and fields for its
// interpreter entry wrappers never ends, and the AOT compiler would overflow its stack on their seeds.
public struct AotSeedingMemoryNode
{
	public ReadOnlyMemory<AotSeedingMemoryNode> Children;
	public int Value;
}

public struct AotSeedingSegmentNode
{
	public ArraySegment<AotSeedingSegmentNode> Children;
	public int Value;
}

public struct AotSeedingArrayWrapper<T>
{
	public T[] Items;
}

public struct AotSeedingWrapperNode
{
	public AotSeedingArrayWrapper<AotSeedingWrapperNode> Children;
	public int Value;
}

public struct AotSeedingGenericNode<T>
{
	public ReadOnlyMemory<AotSeedingGenericNode<T>> Children;
	public T Value;
}

public interface IAotSeedingTrees
{
	void Take(AotSeedingMemoryNode a, int b, int c, int d, int e, int f, int g, int h, int i);

	void Take(AotSeedingSegmentNode a);

	void Take(AotSeedingWrapperNode a, int b);

	void Take(ReadOnlyMemory<AotSeedingMemoryNode> a, short b);

	void Take(AotSeedingGenericNode<int> a, int b, int c);

	void Take(AotSeedingGenericNode<long> a, int b, int c, int d);
}

// Legal generic structs with 15 and 16 type arguments: get_wrapper_shared_type_full asserts that a generic struct has fewer than 16 before it maps
// them, so the AOT compiler would stop on a seed that takes or returns one by value, directly, through a field or through a type argument of a
// generic struct.
public struct AotSeedingWide15<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15>
{
	public T1 First;
	public T15 Last;
}

public struct AotSeedingWide16<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16>
{
	public T1 First;
	public T16 Last;
}

public struct AotSeedingWide15Holder
{
	public AotSeedingWide15<int, int, int, int, int, int, int, int, int, int, int, int, int, int, int> Wide;
	public int Value;
}

public struct AotSeedingWide16Holder
{
	public AotSeedingWide16<int, int, int, int, int, int, int, int, int, int, int, int, int, int, int, int> Wide;
	public int Value;
}

public interface IAotSeedingWideStructs
{
	void Take(AotSeedingWide15<int, int, int, int, int, int, int, int, int, int, int, int, int, int, int> a);

	void Take(AotSeedingWide16<int, int, int, int, int, int, int, int, int, int, int, int, int, int, int, int> a);

	void Take(AotSeedingWide15Holder a, int b);

	void Take(AotSeedingWide16Holder a, int b);

	void Take(AotSeedingDepth<AotSeedingWide15<int, int, int, int, int, int, int, int, int, int, int, int, int, int, int>> a, int b, int c);

	void Take(AotSeedingDepth<AotSeedingWide16<int, int, int, int, int, int, int, int, int, int, int, int, int, int, int, int>> a, int b, int c);

	AotSeedingWide16<int, int, int, int, int, int, int, int, int, int, int, int, int, int, int, int> Make(short a, short b, short c);

	// The runtime passes these without mapping the struct.
	void Take(List<AotSeedingWide16<int, int, int, int, int, int, int, int, int, int, int, int, int, int, int, int>> a, int b, int c, int d);

	void Take(AotSeedingWide16<int, int, int, int, int, int, int, int, int, int, int, int, int, int, int, int>[] a, short b);

	void Take(in AotSeedingWide16<int, int, int, int, int, int, int, int, int, int, int, int, int, int, int, int> a, long b);
}

// Generic virtual methods and generic static abstract members: a call names the method it overrides or implements, and the runtime picks the
// implementation (AotSeedingGenericVirtualStores.Use), whose code creates the store over each key. And more ways the runtime reaches generic code.
public static class AotSeedingGenericVirtualStores
{
	public static object Create<T>() where T : IAotSeedingGenericFactory => T.CreateAbstract<AotSeedingGenericStaticAbstractKey>();

	public static object CreateByDefault<T>() where T : IAotSeedingGenericDefaultFactory => T.Create<AotSeedingGenericStaticDefaultKey>();

	public static object CreateInherited<T>() where T : IAotSeedingFactory => T.Create();

	public static T Activate<T>() => Activator.CreateInstance<T>();

	public static T CreateStruct<T>() where T : struct => new T();

	public static object[] Use() =>
	[
		((IAotSeedingGenericVisitor)new AotSeedingGenericVisitor()).Visit<AotSeedingGenericInterfaceMethodKey>(),
		((AotSeedingAbstractVisitor)new AotSeedingClassVisitor()).VisitAbstract<AotSeedingGenericAbstractMethodKey>(),
		((AotSeedingVirtualVisitor)new AotSeedingOverridingVisitor()).VisitVirtual<AotSeedingGenericOverrideKey>(),
		new Func<object>(((IAotSeedingGenericVisitor)new AotSeedingGenericVisitor()).Visit<AotSeedingGenericMethodDelegateKey>)(),
		Create<AotSeedingGenericCreator>(),
		((IAotSeedingDefaultVisitor)new AotSeedingDefaultVisitor()).VisitByDefault<AotSeedingGenericDefaultMethodKey>(),
		CreateByDefault<AotSeedingGenericDefaultFactory>(),
		Activate<AotSeedingActivated<AotSeedingExplicitActivatorKey>>().Store,
		CreateStruct<AotSeedingStructStore<AotSeedingStructConstructorKey>>().Store,
		CreateInherited<AotSeedingInheritedCreator<AotSeedingInheritedStaticAbstractKey>>(),
		new AotSeedingLambdaHost<AotSeedingClassLambdaKey>().Make()(),
		AotSeedingStaticStore<AotSeedingStaticFieldKey>.Store,
	];
}

public interface IAotSeedingGenericVisitor
{
	object Visit<T>();
}

public sealed class AotSeedingGenericVisitor : IAotSeedingGenericVisitor
{
	public object Visit<T>() => new AotSeedingStore<T>();
}

public abstract class AotSeedingAbstractVisitor
{
	public abstract object VisitAbstract<T>();
}

public sealed class AotSeedingClassVisitor : AotSeedingAbstractVisitor
{
	public override object VisitAbstract<T>() => new AotSeedingStore<T>();
}

public class AotSeedingVirtualVisitor
{
	public virtual object VisitVirtual<T>() => "";
}

public sealed class AotSeedingOverridingVisitor : AotSeedingVirtualVisitor
{
	public override object VisitVirtual<T>() => new AotSeedingStore<T>();
}

public interface IAotSeedingGenericFactory
{
	static abstract object CreateAbstract<U>();
}

public sealed class AotSeedingGenericCreator : IAotSeedingGenericFactory
{
	public static object CreateAbstract<U>() => new AotSeedingStore<U>();
}

public interface IAotSeedingDefaultVisitor
{
	object VisitByDefault<T>() => new AotSeedingStore<T>();
}

public sealed class AotSeedingDefaultVisitor : IAotSeedingDefaultVisitor
{
}

public interface IAotSeedingGenericDefaultFactory
{
	static virtual object Create<U>() => new AotSeedingStore<U>();
}

public sealed class AotSeedingGenericDefaultFactory : IAotSeedingGenericDefaultFactory
{
}

public interface IAotSeedingFactory
{
	static abstract object Create();
}

public class AotSeedingFactoryBase<T>
{
	public static object Create() => new AotSeedingStore<T>();
}

public sealed class AotSeedingInheritedCreator<T> : AotSeedingFactoryBase<T>, IAotSeedingFactory
{
}

public sealed class AotSeedingActivated<T>
{
	public readonly object Store = new AotSeedingStore<T>();
}

public struct AotSeedingStructStore<T>
{
	public object Store;

	public AotSeedingStructStore() => Store = new AotSeedingStore<T>();
}

public sealed class AotSeedingLambdaHost<T>
{
	public Func<object> Make() => () => new AotSeedingStore<T>();
}

public static class AotSeedingStaticStore<T>
{
	public static readonly object Store = new AotSeedingStore<T>();
}

[StructLayout(LayoutKind.Explicit)] public struct AotSeedingGenericInterfaceMethodKey { [FieldOffset(0)] public int Value; }
[StructLayout(LayoutKind.Explicit)] public struct AotSeedingGenericAbstractMethodKey { [FieldOffset(0)] public int Value; }
[StructLayout(LayoutKind.Explicit)] public struct AotSeedingGenericOverrideKey { [FieldOffset(0)] public int Value; }
[StructLayout(LayoutKind.Explicit)] public struct AotSeedingGenericMethodDelegateKey { [FieldOffset(0)] public int Value; }
[StructLayout(LayoutKind.Explicit)] public struct AotSeedingGenericStaticAbstractKey { [FieldOffset(0)] public int Value; }
[StructLayout(LayoutKind.Explicit)] public struct AotSeedingGenericDefaultMethodKey { [FieldOffset(0)] public int Value; }
[StructLayout(LayoutKind.Explicit)] public struct AotSeedingGenericStaticDefaultKey { [FieldOffset(0)] public int Value; }
[StructLayout(LayoutKind.Explicit)] public struct AotSeedingExplicitActivatorKey { [FieldOffset(0)] public int Value; }
[StructLayout(LayoutKind.Explicit)] public struct AotSeedingStructConstructorKey { [FieldOffset(0)] public int Value; }
[StructLayout(LayoutKind.Explicit)] public struct AotSeedingInheritedStaticAbstractKey { [FieldOffset(0)] public int Value; }
[StructLayout(LayoutKind.Explicit)] public struct AotSeedingClassLambdaKey { [FieldOffset(0)] public int Value; }
[StructLayout(LayoutKind.Explicit)] public struct AotSeedingStaticFieldKey { [FieldOffset(0)] public int Value; }
