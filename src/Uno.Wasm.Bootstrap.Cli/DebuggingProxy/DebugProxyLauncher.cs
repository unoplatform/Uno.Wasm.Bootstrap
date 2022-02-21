// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.
//
// Based on https://github.com/dotnet/aspnetcore/blob/52eff90fbcfca39b7eb58baad597df6a99a542b0/src/Components/WebAssembly/Server/src/DebugProxyLauncher.cs
//
#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.WebSockets;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.CommandLineUtils;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Uno.Wasm.Bootstrap.Cli
{
	internal static class DebugProxyLauncher
	{
		private static readonly TimeSpan DebugProxyLaunchTimeout;
		private static Task<string>? LaunchedDebugProxyUrl;
		private static SemaphoreSlim LaunchLock = new SemaphoreSlim(1);
		private static readonly Regex NowListeningRegex = new Regex(@"^\s*Now listening on: (?<url>.*)$", RegexOptions.None, TimeSpan.FromSeconds(10));
		private static readonly Regex ApplicationStartedRegex = new Regex(@"^\s*Application started\. Press Ctrl\+C to shut down\.$", RegexOptions.None, TimeSpan.FromSeconds(10));

		private static Process? _debugProxyProcess;
		private static string? _devToolsHost;
		private static readonly HttpClient _aliveCheckClient;

		static DebugProxyLauncher()
		{
			if(!TimeSpan.TryParse(Environment.GetEnvironmentVariable("WASMSHELL_DEBUG_KEEPALIVE_TIMEOUT"), out var timespan))
			{
				timespan = TimeSpan.FromSeconds(10);
			}

			_aliveCheckClient = new HttpClient()
			{
				Timeout = timespan
			};

			if (!TimeSpan.TryParse(Environment.GetEnvironmentVariable("WASMSHELL_DEBUG_KEEPALIVE_TIMEOUT"), out DebugProxyLaunchTimeout))
			{
				DebugProxyLaunchTimeout = TimeSpan.FromSeconds(10);
			}
		}

		public static async Task<string> EnsureLaunchedAndGetUrl(IServiceProvider serviceProvider, IConfiguration configuration, string devToolsHost, Uri? browserUrl)
		{
			try
			{
				await LaunchLock.WaitAsync();

				if (LaunchedDebugProxyUrl == null
					|| (_debugProxyProcess?.HasExited ?? false)
					|| (!await IsAlive()))
				{
					LaunchedDebugProxyUrl = LaunchAndGetUrl(serviceProvider, configuration, devToolsHost, browserUrl);
				}

				return await LaunchedDebugProxyUrl;
			}
			finally
			{
				LaunchLock.Release();
			}
		}

		/// <summary>
		/// Determines if the target host is available.
		/// </summary>
		/// <returns></returns>
		private static async Task<bool> IsAlive()
		{
			if (_devToolsHost == null)
			{
				return false;
			}

			try
			{
				_ = await _aliveCheckClient.GetByteArrayAsync($"{_devToolsHost}");
				return true;
			}
			catch
			{
				return false;
			}
		}

		private static async Task<string> LaunchAndGetUrl(IServiceProvider serviceProvider, IConfiguration configuration, string devToolsHost, Uri? browserUrl)
		{
			var tcs = new TaskCompletionSource<string>();

			var environment = serviceProvider.GetRequiredService<IWebHostEnvironment>();
			var executablePath = LocateDebugProxyExecutable(environment, configuration);
			var muxerPath = DotNetMuxer.MuxerPathOrDefault();
			var ownerPid = Process.GetCurrentProcess().Id;

			var processStartInfo = new ProcessStartInfo
			{
				FileName = muxerPath,
				Arguments = $"exec \"{executablePath}\" --OwnerPid {ownerPid} --DevToolsUrl {devToolsHost}",
				UseShellExecute = false,
				RedirectStandardOutput = true,
			};
			RemoveUnwantedEnvironmentVariables(processStartInfo.Environment);

			_devToolsHost = devToolsHost;

			if(_debugProxyProcess != null && !_debugProxyProcess.HasExited)
			{
				_debugProxyProcess.Kill();
			}

			_debugProxyProcess = Process.Start(processStartInfo);
			if (_debugProxyProcess is null)
			{
				tcs.TrySetException(new InvalidOperationException("Unable to start debug proxy process."));
			}
			else
			{
				PassThroughConsoleOutputAndError(_debugProxyProcess);
				CompleteTaskWhenServerIsReady(_debugProxyProcess, tcs);

				new CancellationTokenSource(DebugProxyLaunchTimeout).Token.Register(() =>
				{
					tcs.TrySetException(new TimeoutException($"Failed to start the debug proxy within the timeout period of {DebugProxyLaunchTimeout.TotalSeconds} seconds."));
				});
			}

			return await tcs.Task;
		}

		private static void RemoveUnwantedEnvironmentVariables(IDictionary<string, string?> environment)
		{
			// Generally we expect to pass through most environment variables, since dotnet might
			// need them for arbitrary reasons to function correctly. However, we specifically don't
			// want to pass through any ASP.NET Core hosting related ones, since the child process
			// shouldn't be trying to use the same port numbers, etc. In particular we need to break
			// the association with IISExpress and the MS-ASPNETCORE-TOKEN check.
			// For more context on this, see https://github.com/dotnet/aspnetcore/issues/20308.
			var keysToRemove = environment.Keys.Where(key => key.StartsWith("ASPNETCORE_", StringComparison.Ordinal)).ToList();
			foreach (var key in keysToRemove)
			{
				environment.Remove(key);
			}
		}

		private static string LocateDebugProxyExecutable(IWebHostEnvironment environment, IConfiguration configuration)
		{
			var buildConfiguration = configuration.GetValue<string>("configuration");
			var targetFramework = configuration.GetValue<string>("targetframework");

			var contentRoot = environment.ContentRootPath;
			var debuggerInfoRoot = Path.Combine(contentRoot, "obj", buildConfiguration, targetFramework, "wasm-debugger");

			var debuggerInfoFile = Path.Combine(debuggerInfoRoot, ".debuggerinfo");
			if (!File.Exists(debuggerInfoFile))
			{
				throw new FileNotFoundException(
					$"Cannot start debug proxy because its path cannot be determined.");
			}

			var debugProxyPath = Path.Combine(
				File.ReadAllText(debuggerInfoFile),
				"BrowserDebugHost.dll");

			if (!File.Exists(debugProxyPath))
			{
				throw new FileNotFoundException(
					$"Cannot start debug proxy because it cannot be found at '{debugProxyPath}'");
			}

			return debugProxyPath;
		}

		private static void PassThroughConsoleOutputAndError(Process process)
		{
			process.OutputDataReceived +=
				(sender, eventArgs) => Console.WriteLine(eventArgs.Data);

			process.ErrorDataReceived +=
				(sender, eventArgs) => Console.WriteLine("[DebuggerHost-ERROR] " + eventArgs.Data);
		}

		private static void CompleteTaskWhenServerIsReady(Process aspNetProcess, TaskCompletionSource<string> taskCompletionSource)
		{
			string? capturedUrl = null;
			aspNetProcess.OutputDataReceived += OnOutputDataReceived;
			aspNetProcess.BeginOutputReadLine();

			void OnOutputDataReceived(object sender, DataReceivedEventArgs eventArgs)
			{
				if (string.IsNullOrEmpty(eventArgs.Data))
				{
					taskCompletionSource.TrySetException(new InvalidOperationException(
						"No output has been recevied from the application."));
					return;
				}

				if (ApplicationStartedRegex.IsMatch(eventArgs.Data))
				{
					aspNetProcess.OutputDataReceived -= OnOutputDataReceived;
					if (!string.IsNullOrEmpty(capturedUrl))
					{
						taskCompletionSource.TrySetResult(capturedUrl);
					}
					else
					{
						taskCompletionSource.TrySetException(new InvalidOperationException(
							"The application started listening without first advertising a URL"));
					}
				}
				else
				{
					var match = NowListeningRegex.Match(eventArgs.Data);
					if (match.Success)
					{
						capturedUrl = match.Groups["url"].Value;
					}
				}
			}
		}
	}
}
