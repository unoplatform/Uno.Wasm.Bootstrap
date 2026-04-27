using System;
using System.Collections.Immutable;
using System.Linq;

namespace Uno.VersionChecker;

internal static class VersionCheckReplViews
{
	public static VersionCheckInspectionView CreateInspection(string? toolVersion, VersionCheckReport report) =>
		new(
			"uno-wasm-version",
			toolVersion ?? "unknown",
			report.Target.SiteUri.ToString(),
			report.Server,
			report.UnoConfigUrl,
			report.DotnetConfigUrl,
			report.BootConfigSource,
			report.MainAssemblyName,
			report.Assemblies.Length);

	public static VersionCheckSummaryView CreateSummary(VersionCheckReport report) =>
		new(
			report.MainAssembly?.Name ?? report.MainAssemblyName,
			report.MainAssembly?.Version,
			report.MainAssembly?.Configuration,
			report.UnoUiAssembly?.Version,
			report.RuntimeFrameworkName,
			report.RuntimeVersion,
			report.GlobalizationMode,
			report.LinkerEnabled switch
			{
				true => "enabled",
				false => "disabled",
				_ => null
			},
			report.DebugLevel);

	public static ImmutableArray<VersionCheckAssemblyRow> CreateAssemblyRows(VersionCheckReport report) =>
		report.Assemblies
			.Select(assembly => new VersionCheckAssemblyRow(
				assembly.Name,
				assembly.Version,
				assembly.FileVersion,
				assembly.Configuration,
				assembly.TargetFramework,
				assembly.Commit))
			.ToImmutableArray();

	public static string FormatSuccessMessage(VersionCheckReport report) =>
		report.Assemblies.Length == 1
			? "Inspection completed. Found 1 assembly."
			: $"Inspection completed. Found {report.Assemblies.Length} assemblies.";
}

internal sealed record VersionCheckInspectionView(
	string Tool,
	string ToolVersion,
	string Target,
	string? Server,
	string? UnoConfigUrl,
	string? DotnetConfigUrl,
	string? BootConfigSource,
	string? MainAssembly,
	int AssemblyCount);

internal sealed record VersionCheckSummaryView(
	string? MainAssembly,
	string? MainAssemblyVersion,
	string? MainAssemblyBuild,
	string? UnoUiVersion,
	string? RuntimeFramework,
	string? RuntimeVersion,
	string? GlobalizationMode,
	string? Linker,
	int? DebugLevel);

internal sealed record VersionCheckAssemblyRow(
	string? Name,
	string Version,
	string FileVersion,
	string Build,
	string Framework,
	string? Commit);
