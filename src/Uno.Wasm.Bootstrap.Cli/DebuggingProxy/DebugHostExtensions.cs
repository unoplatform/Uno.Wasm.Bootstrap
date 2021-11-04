#nullable enable

// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

//
// Based on https://github.com/dotnet/aspnetcore/blob/7a3f9fe66b641c7667e9122cbab5e6052525d030/src/Components/WebAssembly/Server/src/WebAssemblyNetDebugProxyAppBuilderExtensions.cs
//

using System;
using System.Net;
using System.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

#if NET6_0_OR_GREATER
using _RequestDelegate = Microsoft.AspNetCore.Http.RequestDelegate;
#else
using _RequestDelegate = System.Func<System.Threading.Tasks.Task>;
#endif

namespace Uno.Wasm.Bootstrap.Cli.DebuggingProxy
{
	internal static class DebuggerHostExtensions
	{
		/// <summary>
		/// Adds middleware needed for debugging Blazor WebAssembly applications
		/// inside Chromium dev tools.
		/// </summary>
		public static void UseWebAssemblyDebugging(this IApplicationBuilder app, IConfiguration configuration)
			=> app.Map("/_framework/debug", app =>
			{
				app.Use(async (HttpContext context, _RequestDelegate next) =>
				{
					var queryParams = HttpUtility.ParseQueryString(context.Request.QueryString.Value!);
					var browserParam = queryParams.Get("browser");
					Uri? browserUrl = null;
					var devToolsHost = "http://localhost:9222";
					if (browserParam != null)
					{
						browserUrl = new Uri(browserParam);
						devToolsHost = $"http://{browserUrl.Host}:{browserUrl.Port}";
					}

					var debugProxyBaseUrl = await DebugProxyLauncher.EnsureLaunchedAndGetUrl(context.RequestServices, configuration, devToolsHost, browserUrl);
					var requestPath = context.Request.Path.ToString();
					if (requestPath == string.Empty)
					{
						requestPath = "/";
					}

					switch (requestPath)
					{
						case "/":
							var targetPickerUi = new TargetPickerUi(debugProxyBaseUrl, devToolsHost);
							await targetPickerUi.Display(context);
							break;
						case "/ws-proxy":
							context.Response.Redirect($"{debugProxyBaseUrl}{browserUrl!.PathAndQuery}");
							break;
						default:
							context.Response.StatusCode = (int)HttpStatusCode.NotFound;
							break;
					}
				});
			});
	}

}
