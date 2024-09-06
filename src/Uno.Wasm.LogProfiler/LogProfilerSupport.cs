#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace Uno
{
	public static class LogProfilerSupport
	{
		private static readonly string ProfilerConfig = Environment.GetEnvironmentVariable("UNO_BOOTSTRAP_LOG_PROFILER_OPTIONS");
		private static readonly Dictionary<string, string> _config;

		static LogProfilerSupport()
		{
			_config = ParseConfig();
		}

		public static void FlushProfile()
		{
#if DEBUG
			Console.WriteLine($"Flushing log profiler buffers (config: {ProfilerConfig})");
#endif
			mono_profiler_flush_log();
		}

		public static string GetProfilerProfileOutputFile()
		{
			if (!_config.TryGetValue("output", out var outputFile))
			{
				outputFile = "output.mlpd";
			}

			return outputFile;
		}

		public static void TriggerHeapShot()
		{
			mono_profiler_flush_log();

			var fileSize = File.Exists(GetProfilerProfileOutputFile())
				? new FileInfo(GetProfilerProfileOutputFile()).Length
				: -1;

			Console.WriteLine($"Recording memory profiler heap shot (File size: {fileSize/1024} KB)");
			Mono.Profiler.Log.LogProfiler.TriggerHeapshot();
		}

		private static Dictionary<string, string> ParseConfig()
		{
			// log:alloc,output=output.mlpd

			if (string.IsNullOrEmpty(ProfilerConfig))
			{
				throw new InvalidOperationException("Invalid log profiler config");
			}

			var parts = ProfilerConfig.Substring("log:".Length).Split(',');

			var config = new Dictionary<string, string>();

			foreach(var part in parts)
			{
				var pair = part.Split('=');

				if (pair.Length == 1)
				{
					config[pair[0]] = pair[0];
				}
				else
				{
					config[pair[0]] = pair[1];
				}
			}

			return config;
		}

		[DllImport("__Native")]
		extern static private void mono_profiler_flush_log();
	}
}
