#if NET10_0
using System;
using System.ComponentModel;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Repl;

namespace Uno.VersionChecker;

using Results = Repl.Results;

internal static class VersionCheckerReplHost
{
	public static ReplApp CreateApp(string? version = null, HttpClient? httpClient = null)
	{
		var app = ReplApp.Create(services =>
			{
				services.AddSingleton(httpClient ?? new HttpClient());
				services.AddSingleton<VersionCheckService>();
			})
			.WithDescription("Inspect a deployed Uno Platform WebAssembly application and extract version information.")
			.WithBanner("""
				Try: inspect https://myapp.example.com
				Try: myapp.example.com
				""")
			.UseDefaultInteractive()
			.UseCliProfile()
			.Options(options => options.Parsing.AddRouteConstraint("site", static value => VersionCheckTarget.TryParse(value, out _)));

		app.Map(
			"{target:site}",
			[Description("Inspect a deployed Uno WebAssembly app by host or URL.")]
			(string target, [FromServices] VersionCheckService service) => InspectTargetAsync(target, service, version));

		app.Map(
			"inspect {target:site}",
			[Description("Inspect a deployed Uno WebAssembly app by host or URL.")]
			(string target, [FromServices] VersionCheckService service) => InspectTargetAsync(target, service, version));

		return app;
	}

	private static async Task<object?> InspectTargetAsync(string input, [FromServices] VersionCheckService service, string? version)
	{
		if (!VersionCheckTarget.TryParse(input, out var target, out var error))
		{
			Colorful.Console.Error.WriteLine(error ?? $"Invalid target '{input}'.", System.Drawing.Color.Red);
			return Results.Exit(250);
		}

		ConsoleReportWriter.WriteBanner(version);
		ConsoleReportWriter.WriteCheckingTarget(target!);

		try
		{
			var report = await service.InspectAsync(target!);
			ConsoleReportWriter.WriteSuccessPreamble(report);
			ConsoleReportWriter.WriteResults(report);
			return Results.Exit(0);
		}
		catch (InvalidOperationException ex)
		{
			Colorful.Console.Error.WriteLine(ex.Message, System.Drawing.Color.Red);
			return Results.Exit(1);
		}
		catch (Exception ex)
		{
			Colorful.Console.Error.WriteLine($"Unable to read uno config: {ex}", System.Drawing.Color.Red);
			return Results.Exit(255);
		}
	}
}
#endif
