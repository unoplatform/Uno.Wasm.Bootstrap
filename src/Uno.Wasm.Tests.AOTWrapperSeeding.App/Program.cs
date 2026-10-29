using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Uno.Wasm.Tests.AOTWrapperSeeding;

public interface IService
{
	// Called through the interface from AOT-compiled code: it's in aot.profile.
	int GetResult();

	// Never runs while profiling, so it stays interpreted. With more than 8 parameters, entering the interpreter for it needs an interp_in wrapper,
	// and no compiled code calls a method of this signature. mono_method_get_imt_slot puts GetResult and GetRecords in the same IMT slot (0 of 19),
	// so the first call to GetResult builds a slot that also resolves GetRecords.
	string GetRecords(string filter, int skip, int take, bool ascending, long from, long to, double minScore, double maxScore, int flags);
}

public sealed class Service : IService
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public int GetResult() => 42;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public string GetRecords(string filter, int skip, int take, bool ascending, long from, long to, double minScore, double maxScore, int flags)
		=> $"{filter} {skip} {take} {ascending} {from} {to} {minScore} {maxScore} {flags}";
}

// The same with a generic interface, used over a reference type: the code of Merger<string> is shared, but the runtime enters the interpreter for its
// methods with the signatures of the instance. Only generic code creates a Merger<string> (MergerFactory.Create<T>).
public interface IMerger<T>
{
	// Called through the interface from AOT-compiled code: it's in aot.profile.
	int GetResult();

	// Never runs while profiling, and its signature has T. mono_method_get_imt_slot puts GetResult and Aggregate in the same IMT slot (4 of 19, with
	// T the first generic parameter of the assembly).
	T Aggregate(T a, double b, T c, double d, T e, double f, T g, double h, T i);
}

public sealed class Merger<T> : IMerger<T>
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public int GetResult() => 42;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public T Aggregate(T a, double b, T c, double d, T e, double f, T g, double h, T i) => a;
}

public static class MergerFactory
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static IMerger<T> Create<T>() => new Merger<T>();
}

// Signatures with types whose wrapper names are easy to get wrong. The build fails with UNOWASM006 if the names the bootstrapper expects for them
// aren't the ones the AOT compiler emits.
public unsafe interface INamingSamples
{
	void Store(\u00C9tat value);

	void Store(\u00C9tat value, int a, int b, int c, int d, int e, int f, int g, int h);

	void Fill(InlineInts values, sbyte a, sbyte b, sbyte c);

	void Put(Box<Packed> box);

	void Put(PackedEight value);

	void Put(PackedWithReference value);

	void Put(PackedBytes value);

	void Put(PackedWithSize value);

	void Call(delegate*<int, void> callback);

	void Call(delegate*<int, void> callback, int a, int b, int c, int d, int e, int f, int g, int h);
}

public sealed unsafe class NamingSamples : INamingSamples
{
	public void Store(\u00C9tat value)
	{
	}

	public void Store(\u00C9tat value, int a, int b, int c, int d, int e, int f, int g, int h)
	{
	}

	public void Fill(InlineInts values, sbyte a, sbyte b, sbyte c)
	{
	}

	public void Put(Box<Packed> box)
	{
	}

	public void Put(PackedEight value)
	{
	}

	public void Put(PackedWithReference value)
	{
	}

	public void Put(PackedBytes value)
	{
	}

	public void Put(PackedWithSize value)
	{
	}

	public void Call(delegate*<int, void> callback)
	{
	}

	public void Call(delegate*<int, void> callback, int a, int b, int c, int d, int e, int f, int g, int h)
	{
	}
}

// More than 8 parameters and a struct the runtime keeps as it is (explicit layout) with a default value: the runtime can't enter this method from
// AOT-compiled code, so the build warns with UNOWASM010. It never runs.
public static class AttributedParameters
{
	public static string Describe(int a, int b, int c, int d, int e, int f, int g, int h, \u00C9tat value = default) => a.ToString();
}

// A non-ASCII name (E with an acute accent). With explicit layout, wrapper names keep the type.
[StructLayout(LayoutKind.Explicit)]
public struct \u00C9tat
{
	[FieldOffset(0)]
	public int Value;
}

[InlineArray(2)]
public struct InlineInts
{
	public int Element;
}

// 5 bytes aligned to 1: the runtime maps it to its fields, (byte, int). It re-inflates Box<Packed> with that tuple before it maps the fields of Box,
// and the tuple, 8 bytes aligned to 4, then maps to (int, int).
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct Packed
{
	public byte Tag;
	public int Value;
}

public struct Box<T>
{
	public T Value;
}

// The packing raises the alignment: 8 bytes aligned to 8, which the runtime maps to a long.
[StructLayout(LayoutKind.Sequential, Pack = 8)]
public struct PackedEight
{
	public int A;
	public int B;
}

// The packing doesn't apply to references: 8 bytes aligned to 4, which the runtime maps to (int, int).
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct PackedWithReference
{
	public byte A;
	public string B;
}

// Two bytes rounded up to 4 bytes aligned to 4, which the runtime maps to an int.
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct PackedBytes
{
	public byte A;
	public byte B;
}

// An explicit size isn't rounded up: 6 bytes aligned to 4, which the runtime maps to an int.
[StructLayout(LayoutKind.Sequential, Pack = 4, Size = 6)]
public struct PackedWithSize
{
	public int A;
}

public static class Program
{
	static void Main(string[] args)
	{
		// Never true: keeps GetRecords, Aggregate and the samples in the app without running them.
		if (args.Length > 100)
		{
			Console.WriteLine(CallGetRecords());
			Console.WriteLine(CallAggregate());
			UseNamingSamples();
			Twins.TwinA.Use();
			Twins.TwinB.Use();
		}

		var result = "AOTWrapperSeeding:" + CallGetResult() + ":" + CallMergerGetResult();
		Console.WriteLine(result);

		// The bootstrap-generated index.html has no #results element; the test reads this one.
		WebAssembly.Runtime.InvokeJS(
			$"(function(){{ var el = document.createElement('div'); el.id = 'results'; el.textContent = '{result}'; document.body.appendChild(el); }})()");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static IService CreateService() => new Service();

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int CallGetResult() => CreateService().GetResult();

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static string CallGetRecords() => CreateService().GetRecords("filter", 0, 10, true, 0, 100, 0.5, 1.0, 0);

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static IMerger<string> CreateMerger() => MergerFactory.Create<string>();

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int CallMergerGetResult() => CreateMerger().GetResult();

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static string CallAggregate() => CreateMerger().Aggregate("a", 0, "c", 0, "e", 0, "g", 0, "i");

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void UseNamingSamples()
	{
		INamingSamples samples = new NamingSamples();
		samples.Store(default);
		samples.Store(default, 0, 0, 0, 0, 0, 0, 0, 0);
		samples.Fill(default, 0, 0, 0);
		samples.Put(default(Box<Packed>));
		samples.Put(default(PackedEight));
		samples.Put(default(PackedWithReference));
		samples.Put(default(PackedBytes));
		samples.Put(default(PackedWithSize));

		unsafe
		{
			samples.Call((delegate*<int, void>)null);
			samples.Call((delegate*<int, void>)null, 0, 0, 0, 0, 0, 0, 0, 0);
		}

		// Interpreted code calls it: compiled code that calls it would abort when it first runs.
		Console.WriteLine(AttributedParameters.Describe(0, 0, 0, 0, 0, 0, 0, 0));
	}
}
