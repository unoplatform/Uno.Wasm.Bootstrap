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
				services.AddSingleton(httpClient ?? VersionCheckHttp.CreateDefaultHttpClient());
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
			return Results.Exit(250, Results.Validation(error ?? $"Invalid target '{input}'."));
		}

		try
		{
			var report = await service.InspectAsync(target!);
			return (
				VersionCheckReplViews.CreateInspection(version, report),
				VersionCheckReplViews.CreateSummary(report),
				VersionCheckReplViews.CreateAssemblyRows(report),
				Results.Success($"Inspection completed. Found {report.Assemblies.Length} assemblies.")
			);
		}
		catch (InvalidOperationException ex)
		{
			return Results.Exit(1, Results.Error("inspection_failed", ex.Message));
		}
		catch (Exception ex)
		{
			return Results.Exit(255, Results.Error("inspection_exception", $"Unable to inspect '{target!.SiteUri}': {ex.Message}"));
		}
	}
}
