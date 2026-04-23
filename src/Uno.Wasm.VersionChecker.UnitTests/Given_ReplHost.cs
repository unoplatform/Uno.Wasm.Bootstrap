#if NET10_0
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
		using var client = new HttpClient(new StubHttpMessageHandler());
		var execution = await CaptureConsoleAsync(() => VersionCheckerReplHost.CreateApp(httpClient: client).Run(["example.com", "--no-logo"]));

		Assert.AreEqual(1, execution.ExitCode);
		StringAssert.Contains(execution.OutputText, "Checking website at address https://example.com/");
	}

	[TestMethod]
	public async Task When_InspectAliasRuns_Then_ExplicitCommandUsesSamePath()
	{
		using var client = new HttpClient(new StubHttpMessageHandler());
		var execution = await CaptureConsoleAsync(() => VersionCheckerReplHost.CreateApp(httpClient: client).Run(["inspect", "example.com", "--no-logo"]));

		Assert.AreEqual(1, execution.ExitCode);
		StringAssert.Contains(execution.OutputText, "Checking website at address https://example.com/");
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

	private sealed class StubHttpMessageHandler : HttpMessageHandler
	{
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			var content = request.RequestUri?.AbsolutePath == "/"
				? "<html><body><script src=\"app.js\"></script></body></html>"
				: "not found";

			var statusCode = request.RequestUri?.AbsolutePath == "/"
				? HttpStatusCode.OK
				: HttpStatusCode.NotFound;

			return Task.FromResult(new HttpResponseMessage(statusCode)
			{
				Content = new StringContent(content)
			});
		}
	}
}
#endif
