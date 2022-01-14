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
		private static GCHandle _handle;
		private static byte[]? _data;
		private static readonly Dictionary<string, string> _config;

		static LogProfilerSupport()
		{
			_config = ParseConfig();
		}

		public static void ReadProfileOutput()
		{
			Console.WriteLine($"Flushing log profiler buffers (config: {ProfilerConfig})");
			mono_profiler_flush_log();

			_data = File.ReadAllBytes(ProfilerProfileOutputFile);

			Console.WriteLine($"Successfully read profile output file ({ProfilerProfileOutputFile}, size: {_data.Length/1024} KB)");

			_handle = GCHandle.Alloc(_data, GCHandleType.Pinned);
		}

		private static string ProfilerProfileOutputFile
		{
			get
			{
				if (!_config.TryGetValue("output", out var outputFile))
				{
					outputFile = "output.mlpd";
				}

				return outputFile;
			}
		}

		public static int GetLogLength()
			=> _data?.Length ?? -1;

		public static IntPtr GetLogPointer()
			=> _handle.AddrOfPinnedObject();

		public static void ReleaseLog()
		{
			_handle.Free();
			_data = null;
		}

		public static void TriggerHeapShot()
		{
			mono_profiler_flush_log();

			var fileSize = File.Exists(ProfilerProfileOutputFile)
				? new FileInfo(ProfilerProfileOutputFile).Length
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
