// #define BENCHMARK_ENABLED

using System;
using System.Threading;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Loggers;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Toolchains.InProcess;
using System.IO;
using BenchmarkDotNet.Attributes;
using System.Text.Json;

namespace Uno.Wasm.Sample
{
	public static class Program
	{
		static void Main(string[] args)
		{
			Console.WriteLine($"RuntimeVersion: {RuntimeInformation.FrameworkDescription}");
			Console.WriteLine($"Mono Runtime Mode: " + Environment.GetEnvironmentVariable("UNO_BOOTSTRAP_MONO_RUNTIME_MODE"));
			Console.WriteLine($"args: " + args.Length);

			var i = 42;
			var now = DateTime.Now.ToString();
			Console.WriteLine($"Main! {i} {now}");

			Run();
		}

		private static void Run() { }
	}
}
