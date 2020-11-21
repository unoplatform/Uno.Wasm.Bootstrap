using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using RayTraceBenchmark;
using WebAssembly;

namespace Uno.Wasm.Test.Empty
{
	class Program
	{
		static int Main(string[] args)
		{
			System.Console.WriteLine($"Mono Runtime Mode: " + Environment.GetEnvironmentVariable("UNO_BOOTSTRAP_MONO_RUNTIME_MODE"));

			var w = Stopwatch.StartNew();
			System.Console.WriteLine($"Start benchmark");

			RayTraceBenchmark.Console.WriteLineCallback = s =>
			{
				System.Console.WriteLine(s);
				Runtime.InvokeJS($"Interop.appendResult(\"{s}\")");
			};

			BenchmarkMain.SaveImageCallback = d =>
			{
				w.Stop();
				System.Console.WriteLine($"Got results {d.Length} {w.Elapsed}");

				d = BenchmarkMain.ConvertRGBToBGRA(d);

				var gch = GCHandle.Alloc(d, GCHandleType.Pinned);
				var pinnedData = gch.AddrOfPinnedObject();

				try
				{
					var str = $"Interop.setImageRawData({pinnedData}, {Benchmark.Width}, {Benchmark.Height})";
					System.Console.WriteLine($"Running {str}");
					Runtime.InvokeJS(str);
				}
				finally
				{
					gch.Free();
				}
			};

			BenchmarkMain.Start();

			return 0;
		}
	}
}
