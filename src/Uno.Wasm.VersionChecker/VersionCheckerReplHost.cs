using System;
using System.ComponentModel;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Repl;

namespace Uno.VersionChecker;

using Results = Repl.Results;

public static class VersionCheckerReplHost
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
			(string target, CancellationToken cancellationToken, [FromServices] VersionCheckService service) => InspectTargetAsync(target, service, version, cancellationToken));

		app.Map(
			"inspect {target:site}",
			[Description("Inspect a deployed Uno WebAssembly app by host or URL.")]
			(string target, CancellationToken cancellationToken, [FromServices] VersionCheckService service) => InspectTargetAsync(target, service, version, cancellationToken));

		return app;
	}

	private static async Task<object?> InspectTargetAsync(string input, [FromServices] VersionCheckService service, string? version, CancellationToken cancellationToken)
	{
		if (!VersionCheckTarget.TryParse(input, out var target, out var error) || target is null)
		{
			return Results.Exit(250, Results.Validation(error ?? $"Invalid target '{VersionCheckTarget.SanitizeForDisplay(input)}'."));
		}

		try
		{
			var report = await service.InspectAsync(target, cancellationToken);
			return (
				VersionCheckReplViews.CreateInspection(version, report),
				VersionCheckReplViews.CreateSummary(report),
				VersionCheckReplViews.CreateAssemblyRows(report),
				Results.Success(VersionCheckReplViews.FormatSuccessMessage(report))
			);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			return Results.Exit(130, Results.Cancelled("Inspection cancelled."));
		}
		catch (InvalidOperationException ex)
		{
			return Results.Exit(1, Results.Error("inspection_failed", ex.Message));
		}
		catch (Exception ex)
		{
			return Results.Exit(255, Results.Error("inspection_exception", $"Unable to inspect '{GetSafeDisplayUri(target.SiteUri)}': {ex.Message}"));
		}
	}

	private static string GetSafeDisplayUri(Uri siteUri)
	{
		if (string.IsNullOrEmpty(siteUri.UserInfo))
		{
			return siteUri.ToString();
		}

		var builder = new UriBuilder(siteUri)
		{
			UserName = string.Empty,
			Password = string.Empty
		};
		return builder.Uri.ToString();
	}
}
