﻿// Adapted from https://github.com/dotnet/aspnetcore/blob/c85baf8db0c72ae8e68643029d514b2e737c9fae/src/Components/WebAssembly/Server/src/ComponentsWebAssemblyApplicationBuilderExtensions.cs

using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Mime;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Net.Http.Headers;

namespace Uno.Wasm.Bootstrap.Server
{
	/// <summary>
	/// Extensions for mapping Uno Platform WebAssembly applications.
	/// </summary>
	public static class AspNetExtensions
	{
		/// <summary>
		/// Configures the application to serve Uno Platform WebAssembly framework files from the path <paramref name="pathPrefix"/>. This path must correspond to a referenced Uno Platform WebAssembly application project.
		/// </summary>
		/// <param name="builder">The <see cref="IApplicationBuilder"/>.</param>
		/// <param name="pathPrefix">The <see cref="PathString"/> that indicates the prefix for the Uno Platform WebAssembly application.</param>
		/// <returns>The <see cref="IApplicationBuilder"/></returns>
		public static IApplicationBuilder UseUnoFrameworkFiles(this IApplicationBuilder builder, PathString pathPrefix)
		{
			if (builder is null)
			{
				throw new ArgumentNullException(nameof(builder));
			}

			var webHostEnvironment = builder.ApplicationServices.GetRequiredService<IWebHostEnvironment>();

			var options = CreateStaticFilesOptions(webHostEnvironment.WebRootFileProvider);

			builder.UseWhen(ctx => ctx.Request.Path.StartsWithSegments(pathPrefix, out var rest),
			subBuilder =>
			{
				subBuilder.Use(async (context, next) =>
				{
					context.Response.Headers.Append("UnoPlatform-Environment", webHostEnvironment.EnvironmentName);

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
						if (Environment.GetEnvironmentVariable("__ASPNETCORE_BROWSER_TOOLS") is string dotnetWasmHotReload)
						{
							context.Response.Headers.Append("ASPNETCORE-BROWSER-TOOLS", dotnetWasmHotReload);
						}
					}

					await next(context);
				});

				subBuilder.UseStaticFiles(options);
			});

			return builder;
		}

		/// <summary>
		/// Configures the application to serve Uno Platform WebAssembly framework files from the root path "/".
		/// </summary>
		/// <param name="applicationBuilder">The <see cref="IApplicationBuilder"/>.</param>
		/// <returns>The <see cref="IApplicationBuilder"/></returns>
		public static IApplicationBuilder UseUnoFrameworkFiles(this IApplicationBuilder applicationBuilder) =>
			UseUnoFrameworkFiles(applicationBuilder, default);

		private static StaticFileOptions CreateStaticFilesOptions(IFileProvider webRootFileProvider)
		{
			var options = new StaticFileOptions
			{
				FileProvider = webRootFileProvider
			};

			var contentTypeProvider = new FileExtensionContentTypeProvider();
			AddMapping(contentTypeProvider, ".dll", MediaTypeNames.Application.Octet);
			AddMapping(contentTypeProvider, ".clr", MediaTypeNames.Application.Octet);
			AddMapping(contentTypeProvider, ".pdb", MediaTypeNames.Application.Octet);
			AddMapping(contentTypeProvider, ".br", MediaTypeNames.Application.Octet);
			AddMapping(contentTypeProvider, ".dat", MediaTypeNames.Application.Octet);
			AddMapping(contentTypeProvider, ".blat", MediaTypeNames.Application.Octet);

			options.ContentTypeProvider = contentTypeProvider;

			// Static files middleware will try to use application/x-gzip as the content
			// type when serving a file with a gz extension. We need to correct that before
			// sending the file.
			options.OnPrepareResponse = fileContext =>
			{
				// At this point we mapped something from the /_framework
				fileContext.Context.Response.Headers.Append(HeaderNames.CacheControl, "no-cache");

				var requestPath = fileContext.Context.Request.Path;
				var fileExtension = Path.GetExtension(requestPath.Value);
				if (string.Equals(fileExtension, ".gz") || string.Equals(fileExtension, ".br"))
				{
					// When we are serving framework files (under _framework/ we perform content negotiation
					// on the accept encoding and replace the path with <<original>>.gz|br if we can serve gzip or brotli content
					// respectively.
					// Here we simply calculate the original content type by removing the extension and apply it
					// again.
					// When we revisit this, we should consider calculating the original content type and storing it
					// in the request along with the original target path so that we don't have to calculate it here.
					var originalPath = Path.GetFileNameWithoutExtension(requestPath.Value);
					if (originalPath != null && contentTypeProvider.TryGetContentType(originalPath, out var originalContentType))
					{
						fileContext.Context.Response.ContentType = originalContentType;
					}
				}
			};

			return options;
		}

		private static void AddMapping(FileExtensionContentTypeProvider provider, string name, string mimeType)
		{
			if (!provider.Mappings.ContainsKey(name))
			{
				provider.Mappings.Add(name, mimeType);
			}
		}
	}

}
