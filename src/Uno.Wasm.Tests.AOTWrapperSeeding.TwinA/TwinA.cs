using System.Runtime.InteropServices;

namespace Uno.Wasm.Tests.AOTWrapperSeeding.Twins;

// Uno.Wasm.Tests.AOTWrapperSeeding.TwinB defines types of the same names, Value and Service. The runtime tells them apart, and so must the seeding.

// With explicit layout, wrapper names keep the type: the interp_in wrappers of the Take methods of both assemblies have the same name.
[StructLayout(LayoutKind.Explicit)]
public struct Value
{
	[FieldOffset(0)]
	public int X;
}

public interface IConsumer
{
	void Take(Value value, int a, int b, int c, int d, int e, int f, int g, int h);

	void Put(Value value);
}

public sealed class Consumer : IConsumer
{
	public void Take(Value value, int a, int b, int c, int d, int e, int f, int g, int h)
	{
	}

	public void Put(Value value)
	{
	}
}

// A signature that no other assembly of the app has, on a class named like one of TwinB.
public interface IBlender
{
	void Blend(long a, double b, long c, double d, long e, double f, long g, double h, long i);
}

public sealed class Service : IBlender
{
	public void Blend(long a, double b, long c, double d, long e, double f, long g, double h, long i)
	{
	}
}

public static class TwinA
{
	public static void Use()
	{
		IConsumer consumer = new Consumer();
		consumer.Take(default, 0, 0, 0, 0, 0, 0, 0, 0);
		consumer.Put(default);

		IBlender blender = new Service();
		blender.Blend(0, 0, 0, 0, 0, 0, 0, 0, 0);
	}
}
