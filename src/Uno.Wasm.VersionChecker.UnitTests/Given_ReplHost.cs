using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Repl;
using Uno.VersionChecker;

namespace Uno.Wasm.VersionChecker.UnitTests;

[TestClass]
[DoNotParallelize]
public sealed class Given_ReplHost
{
	[TestMethod]
	[Description("Verifies the root route accepts hostnames without a scheme and renders the structured report.")]
	public Task When_RootHostnameCommandRuns_Then_SiteConstraintAcceptsHostWithoutScheme() =>
		AssertStructuredExecutionAsync(["example.com", "--no-logo"]);

	[TestMethod]
	[Description("Verifies the explicit inspect alias reaches the same structured inspection pipeline.")]
	public Task When_InspectAliasRuns_Then_ExplicitCommandUsesSamePath() =>
		AssertStructuredExecutionAsync(["inspect", "example.com", "--no-logo"]);

	[TestMethod]
	[Description("Verifies JSON output contains rich structured payloads with real assembly metadata.")]
	public void When_RenderingJson_Then_ReplSerializesStructuredPayloads()
	{
		using var client = new HttpClient(new StubHttpMessageHandler());
		var execution = CaptureExecution(() =>
			VersionCheckerReplHost.CreateApp(httpClient: client)
				.Run(["inspect", "example.com", "--json", "--no-logo"]));

		execution.ExitCode.Should().Be(0);

		var documents = ParseJsonDocuments(execution.OutputText);
		documents.Length.Should().Be(4);

		documents[0].RootElement.GetProperty("target").GetString().Should().Be("https://example.com/");
		documents[0].RootElement.GetProperty("assemblyCount").GetInt32().Should().Be(2);
		documents[1].RootElement.GetProperty("mainAssembly").GetString().Should().Be(VersionCheckerTestAssets.MainAssemblyName);
		documents[1].RootElement.GetProperty("mainAssemblyVersion").GetString().Should().Be(VersionCheckerTestAssets.MainAssemblyVersion);
		documents[1].RootElement.GetProperty("runtimeFramework").GetString().Should().Be(VersionCheckerTestAssets.RuntimeAssemblyFramework);

		var assemblies = documents[2].RootElement.EnumerateArray().ToArray();
		assemblies.Should().HaveCount(2);
		assemblies.Should().Contain(assembly =>
			assembly.GetProperty("name").GetString() == VersionCheckerTestAssets.MainAssemblyName
			&& assembly.GetProperty("version").GetString() == VersionCheckerTestAssets.MainAssemblyVersion
			&& assembly.GetProperty("framework").GetString() == VersionCheckerTestAssets.MainAssemblyFramework);
		assemblies.Should().Contain(assembly =>
			assembly.GetProperty("name").GetString() == VersionCheckerTestAssets.RuntimeAssemblyName
			&& assembly.GetProperty("version").GetString() == VersionCheckerTestAssets.RuntimeAssemblyVersion);

		documents[3].RootElement.GetProperty("message").GetString().Should().Be("Inspection completed. Found 2 assemblies.");
	}

	private static async Task AssertStructuredExecutionAsync(string[] args)
	{
		using var client = new HttpClient(new StubHttpMessageHandler());
		var execution = CaptureExecution(() => VersionCheckerReplHost.CreateApp(httpClient: client).Run(args));

		execution.ExitCode.Should().Be(0);
		execution.OutputText.Should().Contain("AssemblyCount");
		execution.OutputText.Should().Contain("2");
		execution.OutputText.Should().Contain(VersionCheckerTestAssets.MainAssemblyName);
		execution.OutputText.Should().Contain(VersionCheckerTestAssets.RuntimeAssemblyName);

		await Task.CompletedTask;
	}

	private static CommandExecution CaptureExecution(Func<int> run)
	{
		var originalOut = Console.Out;
		var originalError = Console.Error;
		using var output = new StringWriter();

		try
		{
			Console.SetOut(output);
			Console.SetError(output);
			var exitCode = run();
			return new CommandExecution(exitCode, output.ToString());
		}
		finally
		{
			Console.SetOut(originalOut);
			Console.SetError(originalError);
		}
	}

	private static ImmutableArray<JsonDocument> ParseJsonDocuments(string json)
	{
		var documents = ImmutableArray.CreateBuilder<JsonDocument>();
		var index = 0;
		while (TryReadNextJsonBlock(json, ref index, out var block))
		{
			documents.Add(JsonDocument.Parse(block));
		}

		return documents.ToImmutable();
	}

	private static bool TryReadNextJsonBlock(string text, ref int index, out string block)
	{
		while (index < text.Length && char.IsWhiteSpace(text[index]))
		{
			index++;
		}

		if (index >= text.Length)
		{
			block = string.Empty;
			return false;
		}

		var start = index;
		var depth = 0;
		var inString = false;
		var escaping = false;

		for (; index < text.Length; index++)
		{
			var current = text[index];
			if (escaping)
			{
				escaping = false;
				continue;
			}

			if (current == '\\' && inString)
			{
				escaping = true;
				continue;
			}

			if (current == '"')
			{
				inString = !inString;
				continue;
			}

			if (inString)
			{
				continue;
			}

			if (current is '{' or '[')
			{
				depth++;
			}
			else if (current is '}' or ']')
			{
				depth--;
				if (depth == 0)
				{
					index++;
					block = text[start..index];
					return true;
				}
			}
		}

		block = string.Empty;
		return false;
	}

	private sealed record CommandExecution(int ExitCode, string OutputText);

	private sealed class StubHttpMessageHandler : HttpMessageHandler
	{
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
			CancellationToken cancellationToken)
			=> Task.FromResult(request.RequestUri?.AbsolutePath switch
			{
				"/" => Text("""<html><body><script src="pkg/uno-config.js"></script></body></html>"""),
				"/pkg/uno-config.js" => Text("""
					config.uno_app_base = "/pkg";
					config.uno_remote_managedpath = "_framework";
					config.uno_main = "[Uno.Wasm.VersionChecker] Uno.VersionChecker.Program";
					config.assemblies_with_size = {"Uno.Wasm.VersionChecker.dll":1,"System.Private.CoreLib.dll":1};
					"""),
				"/pkg/_framework/Uno.Wasm.VersionChecker.dll" => Bytes(VersionCheckerTestAssets.MainAssemblyBytes),
				"/pkg/_framework/System.Private.CoreLib.dll" => Bytes(VersionCheckerTestAssets.RuntimeAssemblyBytes),
				_ => NotFound()
			});

		private static HttpResponseMessage Text(string content) =>
			new(HttpStatusCode.OK) { Content = new StringContent(content, Encoding.UTF8, "text/plain") };

		private static HttpResponseMessage Bytes(byte[] content) =>
			new(HttpStatusCode.OK) { Content = new ByteArrayContent(content) };

		private static HttpResponseMessage NotFound() =>
			new(HttpStatusCode.NotFound) { Content = new StringContent("not found", Encoding.UTF8, "text/plain") };
	}
}
