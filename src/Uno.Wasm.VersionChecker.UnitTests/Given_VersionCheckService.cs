using System;
using System.Collections.Generic;
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
	[Description("Verifies compressed dotnet.js payloads are decompressed when the HTTP client enables automatic decompression.")]
	public async Task When_DotnetScriptIsGzipCompressed_Then_BootConfigIsExtracted()
	{
		using var client = new HttpClient(new DecompressingStubHttpMessageHandler(request =>
		{
			return request.RequestUri?.AbsolutePath switch
			{
				"/" => StubHttpMessageHandler.Text("""<html><body><script src="pkg/uno-config.js"></script></body></html>"""),
				"/pkg/uno-config.js" => StubHttpMessageHandler.Text("""
						config.uno_app_base = "/pkg";
						config.uno_remote_managedpath = "_framework";
						config.dotnet_js_filename = "dotnet.abc.js";
						"""),
				"/pkg/_framework/dotnet.abc.js" => StubHttpMessageHandler.GzipText("""
						/*json-start*/{"mainAssemblyName":"Uno.Wasm.VersionChecker","resources":{"assembly":{"Uno.Wasm.VersionChecker.dll":"sha","System.Private.CoreLib.dll":"sha"}}}/*json-end*/
						"""),
				"/pkg/_framework/Uno.Wasm.VersionChecker.dll" => StubHttpMessageHandler.Bytes(VersionCheckerTestAssets.MainAssemblyBytes),
				"/pkg/_framework/System.Private.CoreLib.dll" => StubHttpMessageHandler.Bytes(VersionCheckerTestAssets.RuntimeAssemblyBytes),
				_ => StubHttpMessageHandler.NotFound()
			};
		}));
		var service = new VersionCheckService(client);

		var report = await service.InspectAsync(new VersionCheckTarget("https://example.com", new Uri("https://example.com/")));

		report.BootConfigSource.Should().Be("dotnet.abc.js");
		report.Assemblies.Length.Should().Be(2);
		(report.MainAssembly?.Version).Should().Be(VersionCheckerTestAssets.MainAssemblyVersion);
		report.RuntimeVersion.Should().Be(VersionCheckerTestAssets.RuntimeAssemblyVersion);
	}

	[TestMethod]
	[Description("Verifies the compressed dotnet.js flow depends on the default client enabling automatic decompression.")]
	public async Task When_DotnetScriptIsGzipCompressedWithoutAutoDecompression_Then_InspectionFails()
	{
		using var client = new HttpClient(new StubHttpMessageHandler(request =>
		{
			return request.RequestUri?.AbsolutePath switch
			{
				"/" => StubHttpMessageHandler.Text("""<html><body><script src="pkg/uno-config.js"></script></body></html>"""),
				"/pkg/uno-config.js" => StubHttpMessageHandler.Text("""
						config.uno_app_base = "/pkg";
						config.uno_remote_managedpath = "_framework";
						config.dotnet_js_filename = "dotnet.abc.js";
						"""),
				"/pkg/_framework/dotnet.abc.js" => StubHttpMessageHandler.GzipText("""
						/*json-start*/{"mainAssemblyName":"Uno.Wasm.VersionChecker","resources":{"assembly":{"Uno.Wasm.VersionChecker.dll":"sha"}}}/*json-end*/
						"""),
				_ => StubHttpMessageHandler.NotFound()
			};
		}));
		var service = new VersionCheckService(client);

		var act = async () => await service.InspectAsync(new VersionCheckTarget("https://example.com", new Uri("https://example.com/")));

		await act.Should().ThrowAsync<InvalidOperationException>();
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
	[Description("Verifies network-path script references cannot pivot uno-config discovery to another origin.")]
	public async Task When_PageReferencesCrossOriginNetworkPathScript_Then_ScriptIsIgnored()
	{
		var requestedHosts = new List<string>();
		using var client = new HttpClient(new StubHttpMessageHandler(request =>
		{
			requestedHosts.Add(request.RequestUri?.Host ?? string.Empty);
			return (request.RequestUri?.Host, request.RequestUri?.AbsolutePath) switch
			{
				("example.com", "/") => StubHttpMessageHandler.Text("""<html><body><script src="//attacker.example/uno-config.js"></script><script src="pkg/uno-config.js"></script></body></html>"""),
				("example.com", "/pkg/uno-config.js") => StubHttpMessageHandler.Text("""
					config.uno_app_base = "/pkg";
					config.uno_remote_managedpath = "_framework";
					config.uno_main = "[Uno.Wasm.VersionChecker] Uno.VersionChecker.Program";
					config.assemblies_with_size = {"Uno.Wasm.VersionChecker.dll":1};
					"""),
				("example.com", "/pkg/_framework/Uno.Wasm.VersionChecker.dll") => StubHttpMessageHandler.Bytes(VersionCheckerTestAssets.MainAssemblyBytes),
				_ => StubHttpMessageHandler.NotFound()
			};
		}));
		var service = new VersionCheckService(client);

		var report = await service.InspectAsync(new VersionCheckTarget("https://example.com", new Uri("https://example.com/")));

		report.UnoConfigUrl.Should().Be("https://example.com/pkg/uno-config.js");
		requestedHosts.Should().NotContain("attacker.example");
	}

	[TestMethod]
	[Description("Verifies absolute package paths from uno-config.js cannot pivot inspection to another origin.")]
	public async Task When_UnoConfigContainsAbsolutePackagePath_Then_InspectionFails()
	{
		using var client = new HttpClient(new StubHttpMessageHandler(request =>
		{
			return request.RequestUri?.AbsolutePath switch
			{
				"/" => StubHttpMessageHandler.Text("""<html><body><script src="pkg/uno-config.js"></script></body></html>"""),
				"/pkg/uno-config.js" => StubHttpMessageHandler.Text("""
					config.uno_app_base = "https://attacker.invalid/pkg";
					config.uno_remote_managedpath = "_framework";
					config.assemblies_with_size = {"Uno.Wasm.VersionChecker.dll":1};
					"""),
				_ => StubHttpMessageHandler.NotFound()
			};
		}));
		var service = new VersionCheckService(client);

		var act = async () => await service.InspectAsync(new VersionCheckTarget("https://example.com", new Uri("https://example.com/")));

		await act.Should().ThrowAsync<InvalidOperationException>()
			.WithMessage("*outside the inspected site*");
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

	[TestMethod]
	[Description("Verifies a UTF-8 BOM in blazor.boot.json does not prevent boot config discovery.")]
	public async Task When_BootConfigStartsWithUtf8Bom_Then_ItIsStillDetected()
	{
		var bomPrefixedJson = "\uFEFF{\"mainAssemblyName\":\"Uno.Wasm.VersionChecker\",\"resources\":{\"assembly\":{\"Uno.Wasm.VersionChecker.dll\":\"sha\"}}}";
		using var client = new HttpClient(new StubHttpMessageHandler(request =>
		{
			return request.RequestUri?.AbsolutePath switch
			{
				"/" => StubHttpMessageHandler.Text("<html><body></body></html>"),
				"/embedded.js" => StubHttpMessageHandler.NotFound(),
				"/_framework/blazor.boot.json" => StubHttpMessageHandler.Text(bomPrefixedJson),
				"/_framework/Uno.Wasm.VersionChecker.dll" => StubHttpMessageHandler.Bytes(VersionCheckerTestAssets.MainAssemblyBytes),
				_ => StubHttpMessageHandler.NotFound()
			};
		}));
		var service = new VersionCheckService(client);

		var report = await service.InspectAsync(new VersionCheckTarget("https://example.com", new Uri("https://example.com/")));

		report.BootConfigSource.Should().Be("blazor.boot.json");
		report.Assemblies.Length.Should().Be(1);
	}

	[TestMethod]
	[Description("Verifies later duplicate uno-config fields override earlier values while parsing the same config file.")]
	public void When_UnoConfigFieldsRepeat_Then_LastValueWins()
	{
		const string content = """
			config.uno_app_base = "/first";
			config.uno_app_base = "/final";
			config.dotnet_js_filename = "dotnet.first.js";
			config.dotnet_js_filename = "dotnet.final.js";
			""";

		var fields = VersionCheckService.ParseUnoConfigFields(content);

		fields.PackagePath.Should().Be("/final");
		fields.DotnetJsFilename.Should().Be("dotnet.final.js");
	}

	[TestMethod]
	[Description("Verifies a modern bootstrapper layout produces a complete report instead of nullable summary fields.")]
	public async Task When_ModernBootstrapperSiteIsInspected_Then_ReportIsComplete()
	{
		const string packageName = "package_8f4e7c1";
		const string managedPath = "_framework";
		const string dotnetScriptName = "dotnet.8f4e7c1.js";
		var unoConfigPath = $"/{packageName}/uno-config.js";
		var managedBasePath = $"/{packageName}/{managedPath}/";
		using var client = new HttpClient(new StubHttpMessageHandler(request =>
		{
			return request.RequestUri?.AbsolutePath switch
			{
				"/" => StubHttpMessageHandler.Text($"""<html><body><script src="{packageName}/uno-bootstrap.js"></script></body></html>"""),
				var path when path == unoConfigPath => StubHttpMessageHandler.Text($$"""
					config.uno_app_base = "/{{packageName}}";
					config.uno_remote_managedpath = "{{managedPath}}";
					config.dotnet_js_filename = "{{dotnetScriptName}}";
					"""),
				var path when path == $"{managedBasePath}{dotnetScriptName}" => StubHttpMessageHandler.Text($$"""
					/*json-start*/{
						"mainAssemblyName":"{{VersionCheckerTestAssets.MainAssemblyName}}",
						"globalizationMode":"invariant",
						"linkerEnabled":true,
						"debugLevel":0,
						"resources":{
							"coreAssembly":[
								{"name":"{{VersionCheckerTestAssets.RuntimeAssemblyName}}.dll"}
							],
							"assembly":[
								{"name":"{{VersionCheckerTestAssets.MainAssemblyName}}.dll"},
								{"name":"System.ValueTuple.wasm"}
							]
						}
					}/*json-end*/
					"""),
				var path when path == $"{managedBasePath}{VersionCheckerTestAssets.MainAssemblyName}.dll" => StubHttpMessageHandler.Bytes(VersionCheckerTestAssets.MainAssemblyBytes),
				var path when path == $"{managedBasePath}{VersionCheckerTestAssets.RuntimeAssemblyName}.dll" => StubHttpMessageHandler.Bytes(VersionCheckerTestAssets.RuntimeAssemblyBytes),
				var path when path == $"{managedBasePath}System.ValueTuple.wasm" => StubHttpMessageHandler.Bytes(VersionCheckerTestAssets.WebcilAssemblyBytes),
				_ => StubHttpMessageHandler.NotFound()
			};
		}));
		var service = new VersionCheckService(client);

		var report = await service.InspectAsync(new VersionCheckTarget("https://example.com", new Uri("https://example.com/")));

		report.UnoConfigUrl.Should().Be($"https://example.com{unoConfigPath}");
		report.DotnetConfigUrl.Should().Be($"https://example.com{managedBasePath}{dotnetScriptName}");
		report.BootConfigSource.Should().Be(dotnetScriptName);
		report.MainAssemblyName.Should().Be(VersionCheckerTestAssets.MainAssemblyName);
		report.MainAssembly.Should().NotBeNull();
		report.MainAssembly!.Version.Should().Be(VersionCheckerTestAssets.MainAssemblyVersion);
		report.RuntimeVersion.Should().Be(VersionCheckerTestAssets.RuntimeAssemblyVersion);
		report.RuntimeFrameworkName.Should().Be(VersionCheckerTestAssets.RuntimeAssemblyFramework);
		report.GlobalizationMode.Should().Be("invariant");
		report.LinkerEnabled.Should().BeTrue();
		report.DebugLevel.Should().Be(0);
		report.Assemblies.Should().Contain(assembly => assembly.Name == VersionCheckerTestAssets.MainAssemblyName);
		report.Assemblies.Should().Contain(assembly => assembly.Name == VersionCheckerTestAssets.RuntimeAssemblyName);
		report.Assemblies.Should().Contain(assembly => assembly.Name == "System.ValueTuple");
	}

	private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
	{
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
			Task.FromResult(responder(request));

		public static HttpResponseMessage Text(string content) =>
			new(HttpStatusCode.OK) { Content = new StringContent(content, Encoding.UTF8, "text/plain") };

		public static HttpResponseMessage Bytes(byte[] content) =>
			new(HttpStatusCode.OK) { Content = new ByteArrayContent(content) };

		public static HttpResponseMessage GzipText(string content)
		{
			using var output = new MemoryStream();
			using (var gzip = new GZipStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
			{
				var bytes = Encoding.UTF8.GetBytes(content);
				gzip.Write(bytes, 0, bytes.Length);
			}

			return new(HttpStatusCode.OK)
			{
				Content = new ByteArrayContent(output.ToArray())
				{
					Headers =
					{
						ContentEncoding = { "gzip" },
						ContentType = new("application/javascript")
					}
				}
			};
		}

		public static HttpResponseMessage NotFound() =>
			new(HttpStatusCode.NotFound) { Content = new StringContent("not found", Encoding.UTF8, "text/plain") };
	}

	private sealed class DecompressingStubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
	{
		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			var response = responder(request);
			if (!response.Content.Headers.ContentEncoding.Contains("gzip"))
			{
				return response;
			}

			await using var compressed = await response.Content.ReadAsStreamAsync(cancellationToken);
			await using var gzip = new GZipStream(compressed, CompressionMode.Decompress);
			using var decompressed = new MemoryStream();
			await gzip.CopyToAsync(decompressed, cancellationToken);
			response.Content = new ByteArrayContent(decompressed.ToArray());
			return response;
		}
	}
}
