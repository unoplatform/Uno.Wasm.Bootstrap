using System;

namespace Uno.WasmSample.WithSpace.Aot
{
	class Program
	{
		static void Main(string[] args)
		{
			Console.WriteLine("Main");

			Console.WriteLine("Mono Runtime Mode: " + Environment.GetEnvironmentVariable("UNO_BOOTSTRAP_MONO_RUNTIME_MODE"));
		}
	}
}
