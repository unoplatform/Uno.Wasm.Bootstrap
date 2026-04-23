using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Uno.VersionChecker;

namespace Uno.Wasm.VersionChecker.UnitTests;

[TestClass]
public class Given_ReplHost
{
	[TestMethod]
	public async Task When_RootHostnameCommandRuns_Then_SiteConstraintAcceptsHostWithoutScheme()
	{
		using var client = new HttpClient(new StubHttpMessageHandler(GetAssemblyBytes()));
		var execution = await CaptureConsoleAsync(() => VersionCheckerReplHost.CreateApp(httpClient: client).Run(["example.com", "--no-logo"]));

		Assert.AreEqual(0, execution.ExitCode);
		StringAssert.Contains(execution.OutputText, "Target");
		StringAssert.Contains(execution.OutputText, "https://example.com/");
		StringAssert.Contains(execution.OutputText, "Inspection completed. Found 1 assemblies.");
		StringAssert.Contains(execution.OutputText, "Uno.Wasm.VersionChecker");
	}

	[TestMethod]
	public async Task When_InspectAliasRuns_Then_ExplicitCommandUsesSamePath()
	{
		using var client = new HttpClient(new StubHttpMessageHandler(GetAssemblyBytes()));
		var execution = await CaptureConsoleAsync(() => VersionCheckerReplHost.CreateApp(httpClient: client).Run(["inspect", "example.com", "--no-logo"]));

		Assert.AreEqual(0, execution.ExitCode);
		StringAssert.Contains(execution.OutputText, "MainAssembly");
		StringAssert.Contains(execution.OutputText, "Uno.Wasm.VersionChecker");
	}

	[TestMethod]
	public async Task When_RenderingJson_Then_ReplSerializesStructuredPayloads()
	{
		using var client = new HttpClient(new StubHttpMessageHandler(GetAssemblyBytes()));
		var execution = await CaptureConsoleAsync(() => VersionCheckerReplHost.CreateApp(httpClient: client).Run(["inspect", "example.com", "--json", "--no-logo"]));

		Assert.AreEqual(0, execution.ExitCode);
		StringAssert.Contains(execution.OutputText, "\"target\": \"https://example.com/\"");
		StringAssert.Contains(execution.OutputText, "\"assemblyCount\": 1");
		StringAssert.Contains(execution.OutputText, "\"name\": \"Uno.Wasm.VersionChecker\"");
		StringAssert.Contains(execution.OutputText, "\"message\": \"Inspection completed. Found 1 assemblies.\"");
	}

	private static async Task<CommandExecution> CaptureConsoleAsync(Func<int> run)
	{
		var originalOut = Console.Out;
		var originalError = Console.Error;
		using var stdout = new StringWriter();
		using var stderr = new StringWriter();

		try
		{
			Console.SetOut(stdout);
			Console.SetError(stderr);

			var exitCode = run();
			await stdout.FlushAsync();
			await stderr.FlushAsync();
			return new CommandExecution(exitCode, stdout.ToString() + stderr.ToString());
		}
		finally
		{
			Console.SetOut(originalOut);
			Console.SetError(originalError);
		}
	}

	private sealed record CommandExecution(int ExitCode, string OutputText);

	private static byte[] GetAssemblyBytes() =>
		File.ReadAllBytes(typeof(VersionCheckerReplHost).Assembly.Location);

	private sealed class StubHttpMessageHandler(byte[] assemblyBytes) : HttpMessageHandler
	{
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			var path = request.RequestUri?.AbsolutePath ?? string.Empty;
			if (path == "/")
			{
				return Task.FromResult(CreateTextResponse("<html><body><script src=\"pkg/uno-config.js\"></script></body></html>"));
			}

			if (path == "/pkg/uno-config.js")
			{
				const string unoConfig = """
					config.uno_remote_managedpath = "_framework";
					config.uno_app_base = "/pkg";
					config.uno_main = "[Uno.Wasm.VersionChecker] Uno.VersionChecker.Program";
					config.assemblies_with_size = {"Uno.Wasm.VersionChecker.dll": 1};
					""";

				return Task.FromResult(CreateTextResponse(unoConfig));
			}

			if (path == "/pkg/_framework/Uno.Wasm.VersionChecker.dll")
			{
				return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
				{
					Content = new ByteArrayContent(assemblyBytes)
				});
			}

			return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
			{
				Content = new StringContent("not found")
			});
		}

		private static HttpResponseMessage CreateTextResponse(string content) =>
			new(HttpStatusCode.OK)
			{
				Content = new StringContent(content)
			};
	}
}
