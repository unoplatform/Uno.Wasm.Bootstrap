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
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Net.Http.Headers;
using System;
using System.IO;
using System.Linq;
using System.Net.Mime;
using System.Threading.Tasks;
using Uno.Wasm.Bootstrap.Cli.DebuggingProxy;

namespace Uno.Wasm.Bootstrap.Cli.Server
{
	class Startup
	{
		private readonly char OtherDirectorySeparatorChar = Path.DirectorySeparatorChar == '/' ? '\\' : '/';

		public void ConfigureServices(IServiceCollection services)
		{
			services.AddRouting();
			services.AddResponseCompression(options =>
			{
				options.EnableForHttps = true;
				options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(new[]
				{
					MediaTypeNames.Application.Octet
				});
			});
		}

		public void Configure(IApplicationBuilder app, IConfiguration configuration)
		{

			var webHostEnvironment = app.ApplicationServices.GetRequiredService<IWebHostEnvironment>();

			var useSecureMode = ShouldUseSecureMode(webHostEnvironment, configuration);
			var isDebugBuild = IsDebugBuild(configuration);

			if (!isDebugBuild)
			{
				app.UseResponseCompression();
			}

			app.UseDeveloperExceptionPage();

			app.Use(async (context, next) =>
			{
				context.Response.Headers.Add("Access-Control-Allow-Origin", "*");
				context.Response.Headers.Add("Access-Control-Allow-Methods", "*");
				context.Response.Headers.Add("Access-Control-Allow-Headers", "*");

				if (useSecureMode)
				{
					// Required for SharedArrayBuffer: https://developer.chrome.com/blog/enabling-shared-array-buffer/
					context.Response.Headers.Add("Cross-Origin-Embedder-Policy", "require-corp");
					context.Response.Headers.Add("Cross-Origin-Opener-Policy", "same-origin");
				}

				await next();
			});

			var pathBase = FixupPath(configuration.GetValue<string>("pathbase"));

			app.UseStaticFiles(new StaticFileOptions
			{
				ContentTypeProvider = CreateContentTypeProvider(true),
				FileProvider = new PhysicalFileProvider(pathBase),
        HttpsCompression = isDebugBuild ? HttpsCompressionMode.DoNotCompress : HttpsCompressionMode.Compress,
        OnPrepareResponse = SetCacheHeaders,
				DefaultContentType = MediaTypeNames.Application.Octet,
				ServeUnknownFileTypes = true
			});

			app.UseWebAssemblyDebugging(configuration);

			// foreach(DictionaryEntry entry in Environment.GetEnvironmentVariables())
			// {
			// 	Console.WriteLine($"Env: {entry.Key}={entry.Value}");
			// }

			app.MapWhen(
				ctx => ctx.Request.Path.StartsWithSegments("/_framework/unohotreload"),
				subBuilder =>
				{
					subBuilder.Use(async (HttpContext context, Func<Task> next) =>
					{
						context.Response.Headers.Append("Uno-Environment", webHostEnvironment.EnvironmentName);

						if (webHostEnvironment.IsDevelopment())
						{
							// DOTNET_MODIFIABLE_ASSEMBLIES is used by the runtime to initialize hot-reload specific environment variables and is configured
							// by the launching process (dotnet-watch / Visual Studio).
							// In Development, we'll transmit the environment variable to WebAssembly as a HTTP header. The bootstrapping code will read the header
							// and configure it as env variable for the wasm app.
							if (Environment.GetEnvironmentVariable("DOTNET_MODIFIABLE_ASSEMBLIES") is string dotnetModifiableAssemblies)
							{
								context.Response.Headers.Append("DOTNET-MODIFIABLE-ASSEMBLIES", dotnetModifiableAssemblies);
							}

							// See https://github.com/dotnet/aspnetcore/issues/37357#issuecomment-941237000
							// Translate the _ASPNETCORE_BROWSER_TOOLS environment configured by the browser tools agent in to a HTTP response header.
							if (Environment.GetEnvironmentVariable("__ASPNETCORE_BROWSER_TOOLS") is string browserTools)
							{
								context.Response.Headers.Append("ASPNETCORE-BROWSER-TOOLS", browserTools);
							}
						}

						context.Response.StatusCode = 200;
						await context.Response.WriteAsync("");
					});
				});

			// Use SPA fallback routing (serve default page for anything else,
			// excluding /_framework/*)
			app.MapWhen(IsNotFrameworkDir, childAppBuilder =>
			{
				var indexHtmlPath = FindIndexHtmlFile(pathBase);
				var indexHtmlStaticFileOptions = string.IsNullOrEmpty(indexHtmlPath)
					? null : new StaticFileOptions
					{
						FileProvider = new PhysicalFileProvider(Path.GetDirectoryName(indexHtmlPath)),
						HttpsCompression = isDebugBuild ? HttpsCompressionMode.DoNotCompress : HttpsCompressionMode.Compress,
						OnPrepareResponse = SetCacheHeaders
					};

				childAppBuilder.UseSpa(spa =>
				{
					spa.Options.DefaultPageStaticFileOptions = indexHtmlStaticFileOptions;
				});
			});
		}

		private static bool IsDebugBuild(IConfiguration configuration)
		{
			var buildConfiguration = configuration.GetValue<string>("configuration") ?? "";

			return buildConfiguration.Equals("Debug", StringComparison.OrdinalIgnoreCase);
		}

		private static bool ShouldUseSecureMode(IWebHostEnvironment environment, IConfiguration configuration)
		{
			var platform = configuration.GetValue<string>("platform") ?? "";
			var buildConfiguration = configuration.GetValue<string>("configuration") ?? "";
			var targetFramework = configuration.GetValue<string>("targetframework") ?? "";

			var contentRoot = environment.ContentRootPath;
			var debuggerInfoRoot = Path.Combine(contentRoot, "obj", platform, buildConfiguration, targetFramework);

			var featuresInfoFile = Path.Combine(debuggerInfoRoot, ".unoappfeatures");
			if (File.Exists(featuresInfoFile))
			{
				var featuresRaw = File.ReadAllText(featuresInfoFile);

				return featuresRaw.Contains("threads", StringComparison.Ordinal);
			}

			return false;
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
			result.Mappings.Add(".blat", MediaTypeNames.Application.Octet);
			result.Mappings.Add(".br", MediaTypeNames.Application.Octet);
			result.Mappings.Add(".clr", MediaTypeNames.Application.Octet);
			result.Mappings.Add(".dat", MediaTypeNames.Application.Octet);

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
