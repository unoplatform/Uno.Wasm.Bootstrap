// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

//
// Imported from https://github.com/aspnet/Blazor/tree/release/0.7.0
//
// History:
//  2019-01-14: Adjustments to make the debugger imported.
//

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Net.Http.Headers;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Mime;
using System.Reflection;
using System.Text;
using Uno.Wasm.Bootstrap.Cli.DebuggingProxy;

namespace Uno.Wasm.Bootstrap.Cli.Server
{
	class Startup
	{
		private const string WasmMimeType = "application/wasm";
		private readonly char OtherDirectorySeparatorChar = Path.DirectorySeparatorChar == '/' ? '\\' : '/';

		public void ConfigureServices(IServiceCollection services)
		{
			services.AddRouting();
			services.AddResponseCompression(options =>
			{
				options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(new[]
				{
					MediaTypeNames.Application.Octet,
					WasmMimeType
				});
			});
		}

		private void RegisterDebuggerLookup(IApplicationBuilder app, IConfiguration configuration)
		{
			var buildConfiguration = configuration.GetValue<string>("configuration");
			var targetFramework = configuration.GetValue<string>("targetframework");

			var env = app.ApplicationServices.GetRequiredService<IHostingEnvironment>();
			var contentRoot = env.ContentRootPath;

			var ctx = System.Runtime.Loader.AssemblyLoadContext.GetLoadContext(typeof(Startup).Assembly);
			bool enumeratingDebuggerFiles = false;

			Assembly contextResolve(object s, AssemblyName e)
			{
				//
				// Resolve the debugger content from the files copied over from the Wasm SDK folder.
				//
				var isDebuggerFile = e.Name.StartsWith("Mono.Cecil")
					|| e.Name.Contains("Mono.WebAssembly.DebuggerProxy")
					|| e.Name.Contains("BrowserDebugProxy");

				if (!enumeratingDebuggerFiles && isDebuggerFile)
				{
					try
					{
						enumeratingDebuggerFiles = true;

						var debuggerRoot = Path.Combine(contentRoot, "obj", buildConfiguration, targetFramework, "wasm-debugger");

						if (Directory.Exists(debuggerRoot))
						{
							var debuggerLookupPath = Directory.GetDirectories(debuggerRoot).First();
							return Assembly.LoadFrom(Path.Combine(debuggerLookupPath, e.Name + ".dll"));
						}
					}
					finally
					{
						enumeratingDebuggerFiles = false;
					}
				}

				return null;
			}

			ctx.Resolving += contextResolve;
		}

		public void Configure(IApplicationBuilder app, IConfiguration configuration)
		{
			RegisterDebuggerLookup(app, configuration);

			app.UseDeveloperExceptionPage();
			var pathBase = FixupPath(configuration.GetValue<string>("pathbase"));
			var env = app.ApplicationServices.GetRequiredService<IHostingEnvironment>();
			var contentRoot = env.ContentRootPath;

			app.UseStaticFiles(new StaticFileOptions
			{
				FileProvider = new PhysicalFileProvider(pathBase),
				ContentTypeProvider = CreateContentTypeProvider(true),
				OnPrepareResponse = SetCacheHeaders
			});

			app.UseDebugHost();
			app.UseDebugProxy(new ProxyOptions());

			// Use SPA fallback routing (serve default page for anything else,
			// excluding /_framework/*)
			app.MapWhen(IsNotFrameworkDir, childAppBuilder =>
			{
				var indexHtmlPath = FindIndexHtmlFile(pathBase);
				var indexHtmlStaticFileOptions = string.IsNullOrEmpty(indexHtmlPath)
					? null : new StaticFileOptions
					{
						FileProvider = new PhysicalFileProvider(Path.GetDirectoryName(indexHtmlPath)),
						OnPrepareResponse = SetCacheHeaders
					};

				childAppBuilder.UseSpa(spa =>
				{
					spa.Options.DefaultPageStaticFileOptions = indexHtmlStaticFileOptions;
				});
			});
		}


		private static string FindIndexHtmlFile(string basePath)
		{
			var distIndexHtmlPath = Path.Combine(basePath, "index.html");
			if (File.Exists(distIndexHtmlPath))
			{
				return distIndexHtmlPath;
			}

			// Since there's no index.html, we'll use the default DefaultPageStaticFileOptions,
			// hence we'll look for index.html in the host server app's wwwroot.
			return null;
		}

		private static bool IsNotFrameworkDir(HttpContext context)
			=> !context.Request.Path.StartsWithSegments("/_framework");

		private static IContentTypeProvider CreateContentTypeProvider(bool enableDebugging)
		{
			var result = new FileExtensionContentTypeProvider();
			result.Mappings.Add(".clr", MediaTypeNames.Application.Octet);
			result.Mappings.Add(".dat", MediaTypeNames.Application.Octet);
			// result.Mappings.Add(".wasm", "application/wasm");

			if (enableDebugging)
			{
				result.Mappings.Add(".pdb", MediaTypeNames.Application.Octet);
			}

			return result;
		}

		private static void SetCacheHeaders(StaticFileResponseContext ctx)
		{
			// By setting "Cache-Control: no-cache", we're allowing the browser to store
			// a cached copy of the response, but telling it that it must check with the
			// server for modifications (based on Etag) before using that cached copy.
			// Longer term, we should generate URLs based on content hashes (at least
			// for published apps) so that the browser doesn't need to make any requests
			// for unchanged files.
			var headers = ctx.Context.Response.GetTypedHeaders();
			if (headers.CacheControl == null)
			{
				headers.CacheControl = new CacheControlHeaderValue
				{
					NoCache = true
				};
			}
		}

		/// <summary>
		/// Align paths to fix issues with mixed path
		/// </summary>
		string FixupPath(string path)
			=> path.Replace(OtherDirectorySeparatorChar, Path.DirectorySeparatorChar);
	}
}
