using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Uno.VersionChecker;

namespace Uno.Wasm.VersionChecker.UnitTests;

[TestClass]
public sealed class Given_VersionCheckService
{
	[TestMethod]
	[Description("Verifies compressed dotnet.js payloads are decompressed and parsed through the default HTTP client.")]
	public async Task When_DotnetScriptIsGzipCompressed_Then_BootConfigIsExtracted()
	{
		await using var server = await TestHttpServer.StartAsync();
		server.SetResponder(async context =>
		{
			switch (context.Request.Url?.AbsolutePath)
			{
				case "/":
					await TestHttpServer.WriteTextAsync(context, """<html><body><script src="pkg/uno-config.js"></script></body></html>""");
					break;
				case "/pkg/uno-config.js":
					await TestHttpServer.WriteTextAsync(context, """
						config.uno_app_base = "/pkg";
						config.uno_remote_managedpath = "_framework";
						config.dotnet_js_filename = "dotnet.abc.js";
						""");
					break;
				case "/pkg/_framework/dotnet.abc.js":
					await TestHttpServer.WriteGzipTextAsync(context, """
						/*json-start*/{"mainAssemblyName":"Uno.Wasm.VersionChecker","resources":{"assembly":{"Uno.Wasm.VersionChecker.dll":"sha","System.Private.CoreLib.dll":"sha"}}}/*json-end*/
						""");
					break;
				case "/pkg/_framework/Uno.Wasm.VersionChecker.dll":
					await TestHttpServer.WriteBytesAsync(context, VersionCheckerTestAssets.MainAssemblyBytes, "application/octet-stream");
					break;
				case "/pkg/_framework/System.Private.CoreLib.dll":
					await TestHttpServer.WriteBytesAsync(context, VersionCheckerTestAssets.RuntimeAssemblyBytes, "application/octet-stream");
					break;
				default:
					context.Response.StatusCode = (int)HttpStatusCode.NotFound;
					context.Response.Close();
					break;
			}
		});

		using var client = VersionCheckHttp.CreateDefaultHttpClient();
		var service = new VersionCheckService(client);

		var report = await service.InspectAsync(new VersionCheckTarget(server.BaseAddress, new Uri(server.BaseAddress)));

		report.BootConfigSource.Should().Be("dotnet.abc.js");
		report.Assemblies.Length.Should().Be(2);
		(report.MainAssembly?.Version).Should().Be(VersionCheckerTestAssets.MainAssemblyVersion);
		report.RuntimeVersion.Should().Be(VersionCheckerTestAssets.RuntimeAssemblyVersion);
	}

	[TestMethod]
	[Description("Verifies embedded.js package fallback resolves uno-config.js when the HTML page does not reference it directly.")]
	public async Task When_EmbeddedPackageScriptIsPresent_Then_PackageUnoConfigIsResolved()
	{
		using var client = new HttpClient(new StubHttpMessageHandler(request =>
		{
			return request.RequestUri?.AbsolutePath switch
			{
				"/" => StubHttpMessageHandler.Text("""<html><body><script src="app.js"></script></body></html>"""),
				"/embedded.js" => StubHttpMessageHandler.Text("""const package = "package_hash";"""),
				"/package_hash/uno-config.js" => StubHttpMessageHandler.Text("""
					config.uno_app_base = "/package_hash";
					config.uno_remote_managedpath = "_framework";
					config.uno_main = "[Uno.Wasm.VersionChecker] Uno.VersionChecker.Program";
					config.assemblies_with_size = {"Uno.Wasm.VersionChecker.dll":1};
					"""),
				"/package_hash/_framework/Uno.Wasm.VersionChecker.dll" => StubHttpMessageHandler.Bytes(VersionCheckerTestAssets.MainAssemblyBytes),
				_ => StubHttpMessageHandler.NotFound()
			};
		}));
		var service = new VersionCheckService(client);

		var report = await service.InspectAsync(new VersionCheckTarget("https://example.com", new Uri("https://example.com/")));

		report.UnoConfigUrl.Should().Be("https://example.com/package_hash/uno-config.js");
		report.Assemblies.Length.Should().Be(1);
		(report.MainAssembly?.Name).Should().Be(VersionCheckerTestAssets.MainAssemblyName);
	}

	[TestMethod]
	[Description("Verifies uno-bootstrap.js links are rewritten to uno-config.js before config parsing.")]
	public async Task When_PageReferencesUnoBootstrap_Then_UnoConfigIsLoaded()
	{
		using var client = new HttpClient(new StubHttpMessageHandler(request =>
		{
			return request.RequestUri?.AbsolutePath switch
			{
				"/" => StubHttpMessageHandler.Text("""<html><body><script src="pkg/uno-bootstrap.js"></script></body></html>"""),
				"/pkg/uno-config.js" => StubHttpMessageHandler.Text("""
					config.uno_app_base = "/pkg";
					config.uno_remote_managedpath = "_framework";
					config.uno_main = "[Uno.Wasm.VersionChecker] Uno.VersionChecker.Program";
					config.assemblies_with_size = {"Uno.Wasm.VersionChecker.dll":1};
					"""),
				"/pkg/_framework/Uno.Wasm.VersionChecker.dll" => StubHttpMessageHandler.Bytes(VersionCheckerTestAssets.MainAssemblyBytes),
				_ => StubHttpMessageHandler.NotFound()
			};
		}));
		var service = new VersionCheckService(client);

		var report = await service.InspectAsync(new VersionCheckTarget("https://example.com", new Uri("https://example.com/")));

		report.UnoConfigUrl.Should().Be("https://example.com/pkg/uno-config.js");
		(report.MainAssembly?.Version).Should().Be(VersionCheckerTestAssets.MainAssemblyVersion);
	}

	[TestMethod]
	[Description("Verifies PE and WebCIL payloads are both parsed into assembly metadata.")]
	public async Task When_AssembliesContainPeAndWebcil_Then_BothFormatsAreParsed()
	{
		using var client = new HttpClient(new StubHttpMessageHandler(request =>
		{
			return request.RequestUri?.AbsolutePath switch
			{
				"/" => StubHttpMessageHandler.Text("""<html><body><script src="pkg/uno-config.js"></script></body></html>"""),
				"/pkg/uno-config.js" => StubHttpMessageHandler.Text("""
					config.uno_app_base = "/pkg";
					config.uno_remote_managedpath = "_framework";
					config.uno_main = "[Uno.Wasm.VersionChecker] Uno.VersionChecker.Program";
					config.assemblies_with_size = {"Uno.Wasm.VersionChecker.dll":1,"System.ValueTuple.wasm":1};
					"""),
				"/pkg/_framework/Uno.Wasm.VersionChecker.dll" => StubHttpMessageHandler.Bytes(VersionCheckerTestAssets.MainAssemblyBytes),
				"/pkg/_framework/System.ValueTuple.wasm" => StubHttpMessageHandler.Bytes(VersionCheckerTestAssets.WebcilAssemblyBytes),
				_ => StubHttpMessageHandler.NotFound()
			};
		}));
		var service = new VersionCheckService(client);

		var report = await service.InspectAsync(new VersionCheckTarget("https://example.com", new Uri("https://example.com/")));

		report.Assemblies.Length.Should().Be(2);
		report.Assemblies.Any(assembly => assembly.Name == VersionCheckerTestAssets.MainAssemblyName).Should().BeTrue();
		report.Assemblies.Any(assembly => assembly.Name == "System.ValueTuple").Should().BeTrue();
	}

	private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
	{
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
			Task.FromResult(responder(request));

		public static HttpResponseMessage Text(string content) =>
			new(HttpStatusCode.OK) { Content = new StringContent(content, Encoding.UTF8, "text/plain") };

		public static HttpResponseMessage Bytes(byte[] content) =>
			new(HttpStatusCode.OK) { Content = new ByteArrayContent(content) };

		public static HttpResponseMessage NotFound() =>
			new(HttpStatusCode.NotFound) { Content = new StringContent("not found", Encoding.UTF8, "text/plain") };
	}

	private sealed class TestHttpServer : IAsyncDisposable
	{
		private readonly HttpListener _listener;
		private readonly CancellationTokenSource _stop = new();
		private Func<HttpListenerContext, Task> _responder = _ => Task.CompletedTask;
		private readonly Task _loop;

		private TestHttpServer(HttpListener listener)
		{
			_listener = listener;
			_loop = Task.Run(async () =>
			{
				while (!_stop.IsCancellationRequested)
				{
					HttpListenerContext context = null!;
					try
					{
						context = await _listener.GetContextAsync();
						await _responder(context);
					}
					catch (ObjectDisposedException) when (_stop.IsCancellationRequested)
					{
						break;
					}
					catch (HttpListenerException) when (_stop.IsCancellationRequested)
					{
						break;
					}
					finally
					{
						context?.Response.Close();
					}
				}
			});
		}

		public string BaseAddress => _listener.Prefixes.Cast<string>().Single();

		public static Task<TestHttpServer> StartAsync()
		{
			var port = GetAvailablePort();
			var listener = new HttpListener();
			listener.Prefixes.Add($"http://127.0.0.1:{port}/");
			listener.Start();
			return Task.FromResult(new TestHttpServer(listener));
		}

		public void SetResponder(Func<HttpListenerContext, Task> responder) => _responder = responder;

		public async ValueTask DisposeAsync()
		{
			_stop.Cancel();
			_listener.Close();
			await _loop;
			_stop.Dispose();
		}

		public static Task WriteTextAsync(HttpListenerContext context, string content)
		{
			context.Response.StatusCode = (int)HttpStatusCode.OK;
			context.Response.ContentType = "text/plain; charset=utf-8";
			var bytes = Encoding.UTF8.GetBytes(content);
			return context.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
		}

		public static Task WriteBytesAsync(HttpListenerContext context, byte[] content, string contentType)
		{
			context.Response.StatusCode = (int)HttpStatusCode.OK;
			context.Response.ContentType = contentType;
			return context.Response.OutputStream.WriteAsync(content, 0, content.Length);
		}

		public static async Task WriteGzipTextAsync(HttpListenerContext context, string content)
		{
			context.Response.StatusCode = (int)HttpStatusCode.OK;
			context.Response.ContentType = "application/javascript; charset=utf-8";
			context.Response.AddHeader("Content-Encoding", "gzip");
			await using var gzip = new GZipStream(context.Response.OutputStream, CompressionLevel.SmallestSize, leaveOpen: true);
			var bytes = Encoding.UTF8.GetBytes(content);
			await gzip.WriteAsync(bytes, 0, bytes.Length);
			await gzip.FlushAsync();
		}

		private static int GetAvailablePort()
		{
			var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
			listener.Start();
			var port = ((IPEndPoint)listener.LocalEndpoint).Port;
			listener.Stop();
			return port;
		}
	}
}
