using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using RayTraceBenchmark;
using WebAssembly;

namespace Uno.Wasm.Sample.RayTracer.Worker;

class Program
{
	static int Main(string[] args)
	{
		System.Console.WriteLine("Worker: Starting ray tracer benchmark...");

		var sw = Stopwatch.StartNew();

		RayTraceBenchmark.Console.WriteLineCallback = s =>
		{
			System.Console.WriteLine($"Worker: {s}");
			Runtime.InvokeJS($"self.postMessage({{ type: 'raytracer-log', text: '{s}' }})");
		};

		BenchmarkMain.SaveImageCallback = rgbData =>
		{
			sw.Stop();
			System.Console.WriteLine($"Worker: Render complete ({sw.Elapsed})");

			// Convert RGB to BGRA (same as the host does)
			var bgraData = BenchmarkMain.ConvertRGBToBGRA(rgbData);

			// Pin the BGRA buffer and encode as base64 to send via postMessage.
			// We can't send a WASM memory pointer across threads, so we serialize.
			var base64 = Convert.ToBase64String(bgraData);

			// Post the result to the host with dimensions and timing
			Runtime.InvokeJS(
				$"self.postMessage({{ type: 'raytracer-result', width: {Benchmark.Width}, height: {Benchmark.Height}, elapsed: '{sw.Elapsed}', base64: '{base64}' }})");

			System.Console.WriteLine("Worker: Posted result to host.");
		};

		BenchmarkMain.Start();

		return 0;
	}
}
