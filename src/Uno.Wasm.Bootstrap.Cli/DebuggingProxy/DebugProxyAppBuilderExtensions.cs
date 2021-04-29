// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

//
// Imported from https://github.com/aspnet/Blazor/tree/release/0.7.0
//

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection.Metadata.Ecma335;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

#if NETCOREAPP3_1
using WebAssembly.Net.Debugging;
#elif NET5_0
using Microsoft.WebAssembly.Diagnostics;
#endif

namespace Uno.Wasm.Bootstrap.Cli.DebuggingProxy
{
	public class ProxyOptions
	{
		public Uri DevToolsUrl { get; set; } = new Uri("http://localhost:9222");
	}

	static class DebugExtensions
	{
		public static Dictionary<string, string> MapValues(Dictionary<string, string> response, HttpContext context, Uri debuggerHost)
		{
			var filtered = new Dictionary<string, string>();
			var request = context.Request;

			foreach (var key in response.Keys)
			{
				switch (key)
				{
					case "devtoolsFrontendUrl":
						var front = response[key];
						filtered[key] = $"{debuggerHost.Scheme}://{debuggerHost.Authority}{front.Replace($"ws={debuggerHost.Authority}", $"ws={request.Host}")}";
						break;
					case "webSocketDebuggerUrl":
						var page = new Uri(response[key]);
						filtered[key] = $"{page.Scheme}://{request.Host}{page.PathAndQuery}";
						break;
					default:
						filtered[key] = response[key];
						break;
				}
			}
			return filtered;
		}

		public static IApplicationBuilder UseDebugProxy(this IApplicationBuilder app, ProxyOptions options) =>
			UseDebugProxy(app, options, MapValues);

		public static IApplicationBuilder UseDebugProxy(
			this IApplicationBuilder app,
			ProxyOptions options,
			Func<Dictionary<string, string>, HttpContext, Uri, Dictionary<string, string>> mapFunc)
		{
			var devToolsHost = options.DevToolsUrl;
			app.UseRouter(router => {
				router.MapGet("json", RewriteArray);
				router.MapGet("json/list", RewriteArray);
				router.MapGet("json/version", RewriteSingle);
				router.MapGet("json/new", RewriteSingle);
				router.MapGet("devtools/page/{pageId}", ConnectProxy);
				router.MapGet("devtools/browser/{pageId}", ConnectProxy);

				string GetEndpoint(HttpContext context)
				{
					var request = context.Request;
					var requestPath = request.Path;
					return $"{devToolsHost.Scheme}://{devToolsHost.Authority}{request.Path}{request.QueryString}";
				}

				async Task RewriteSingle(HttpContext context)
				{
					var version = await ProxyGetJsonAsync<Dictionary<string, string>>(GetEndpoint(context));
					context.Response.ContentType = "application/json";
					await context.Response.WriteAsync(
						JsonSerializer.Serialize(mapFunc(version, context, devToolsHost)));
				}

				async Task RewriteArray(HttpContext context)
				{
					var tabs = await ProxyGetJsonAsync<Dictionary<string, string>[]>(GetEndpoint(context));
					var alteredTabs = tabs.Select(t => mapFunc(t, context, devToolsHost)).ToArray();
					context.Response.ContentType = "application/json";
					await context.Response.WriteAsync(JsonSerializer.Serialize(alteredTabs));
				}

				async Task ConnectProxy(HttpContext context)
				{
					if (!context.WebSockets.IsWebSocketRequest)
					{
						context.Response.StatusCode = 400;
						return;
					}

					var endpoint = new Uri($"ws://{devToolsHost.Authority}{context.Request.Path.ToString()}");
					try
					{
						using var loggerFactory = LoggerFactory.Create(
							builder => builder.AddConsole().AddFilter(null, LogLevel.Trace));
						var proxy = GetProxy(loggerFactory);
						var ideSocket = await context.WebSockets.AcceptWebSocketAsync();

						await proxy.Run(endpoint, ideSocket);
					}
					catch (Exception e)
					{
						Console.WriteLine("got exception {0}", e);
					}
				}
			});
			return app;
		}

		private static DebuggerProxy GetProxy(ILoggerFactory loggerFactory) =>
#if NETCOREAPP3_1
			new DebuggerProxy(loggerFactory);
#elif NET5_0
			new DebuggerProxy(loggerFactory, new List<string>());
#endif

		static async Task<T> ProxyGetJsonAsync<T>(string url)
		{
			using (var httpClient = new HttpClient())
			{
				var response = await httpClient.GetAsync(url);
				return await JsonSerializer.DeserializeAsync<T>(await response.Content.ReadAsStreamAsync());
			}
		}
	}
}
