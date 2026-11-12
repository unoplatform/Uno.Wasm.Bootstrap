using System.Runtime.InteropServices;

namespace Uno.Wasm.Tests.AOTWrapperSeeding.Twins;

// Uno.Wasm.Tests.AOTWrapperSeeding.TwinA defines types of the same names, Value and Service. The runtime tells them apart, and so must the seeding.

[StructLayout(LayoutKind.Explicit)]
public struct Value
{
	[FieldOffset(0)]
	public int X;
}

public interface IStore
{
	void Take(Value value, int a, int b, int c, int d, int e, int f, int g, int h);

	// One parameter more than IConsumer.Put of TwinA: the seeds of both can be in the same seed method.
	void Put(Value value, int count);
}

public sealed class Store : IStore
{
	public void Take(Value value, int a, int b, int c, int d, int e, int f, int g, int h)
	{
	}

	public void Put(Value value, int count)
	{
	}
}

// A signature that no other assembly of the app has, on a class named like one of TwinA.
public interface IMixer
{
	void Mix(double a, long b, double c, long d, double e, long f, double g, long h, double i);
}

public sealed class Service : IMixer
{
	public void Mix(double a, long b, double c, long d, double e, long f, double g, long h, double i)
	{
	}
}

public static class TwinB
{
	public static void Use()
	{
		IStore store = new Store();
		store.Take(default, 0, 0, 0, 0, 0, 0, 0, 0);
		store.Put(default, 0);

		IMixer mixer = new Service();
		mixer.Mix(0, 0, 0, 0, 0, 0, 0, 0, 0);
	}
}
