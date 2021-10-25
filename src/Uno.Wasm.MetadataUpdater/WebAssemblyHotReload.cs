// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

// Based on https://github.com/dotnet/aspnetcore/blob/b569ccf308f5c95b0e1d2a5df2163e0716822db9/src/Components/WebAssembly/WebAssembly/src/HotReload/HotReloadAgent.cs
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using WebAssembly;

namespace Uno.Wasm.MetadataUpdate
{
	[EditorBrowsable(EditorBrowsableState.Never)]
	public static class WebAssemblyHotReload
	{
		private static bool _linkerEnabled;
		private static bool _debugEnabled;

		private static HotReloadAgent? _hotReloadAgent;
		private static readonly UpdateDelta[] _updateDeltas = new[]
		{
			new UpdateDelta(),
		};

		internal static void Initialize()
		{
			bool.TryParse(Environment.GetEnvironmentVariable("UNO_BOOTSTRAP_LOG_METADATA_UPDATES"), out _debugEnabled);

			if (_debugEnabled)
			{
				Console.WriteLine($"InitializeAsync(Linker:{Environment.GetEnvironmentVariable("UNO_BOOTSTRAP_LINKER_ENABLED")})");
			}

			_linkerEnabled = string.Equals(Environment.GetEnvironmentVariable("UNO_BOOTSTRAP_LINKER_ENABLED"), "true", StringComparison.OrdinalIgnoreCase);

			if (_linkerEnabled)
			{
				WebAssembly.Runtime.InvokeJS("console.warn('" +
					"The application was compiled with the IL linker enabled, hot reload is disabled. " +
					"See WasmShellILLinkerEnabled for more details.');");
			}
			else
			{
				_hotReloadAgent = new HotReloadAgent(m =>
				{
					if (_debugEnabled)
					{
						Console.WriteLine(m);
					}
				});
			}
		}

		/// <summary>
		/// For framework use only.
		/// </summary>
		public static void ApplyHotReloadDelta(string moduleIdString, string metadataDelta, string ilDeta)
		{
			if (_linkerEnabled)
			{
				if (_debugEnabled)
				{
					Console.WriteLine($"Skipping ApplyHotReloadDelta({moduleIdString}) the linker is enabled");
				}
				return;
			}

			if (_debugEnabled)
			{
				Console.WriteLine($"ApplyHotReloadDelta({moduleIdString})");
			}

			var moduleId = Guid.Parse(moduleIdString);

			_updateDeltas[0].ModuleId = moduleId;
			_updateDeltas[0].MetadataDelta = Convert.FromBase64String(metadataDelta);
			_updateDeltas[0].ILDelta = Convert.FromBase64String(ilDeta);

			_hotReloadAgent!.ApplyDeltas(_updateDeltas);
		}

		/// <summary>
		/// For framework use only.
		/// </summary>
		public static string GetApplyUpdateCapabilities()
		{
			if (_debugEnabled)
			{
				Console.WriteLine($"ApplyHotReloadDelta");
			}

			var method = typeof(System.Reflection.Metadata.MetadataUpdater).GetMethod("GetCapabilities", BindingFlags.NonPublic | BindingFlags.Static);
			if (method is null)
			{
				return string.Empty;
			}

			var caps = (string)method.Invoke(obj: null, parameters: null)!;

			if (_debugEnabled)
			{
				Console.WriteLine($"ApplyHotReloadDelta = {caps}");
			}

			return caps;
		}
	}
}
