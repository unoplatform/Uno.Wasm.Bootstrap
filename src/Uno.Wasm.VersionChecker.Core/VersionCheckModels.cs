using System;
using System.Collections.Immutable;

namespace Uno.VersionChecker;

public sealed record AssemblyVersionInfo(
	string? Name,
	string Version,
	string FileVersion,
	string Configuration,
	string TargetFramework,
	string? Commit);

public sealed record VersionCheckReport(
	VersionCheckTarget Target,
	string? Server,
	string? UnoConfigUrl,
	string? DotnetConfigUrl,
	string? BootConfigSource,
	string? MainAssemblyName,
	ImmutableArray<AssemblyVersionInfo> Assemblies,
	AssemblyVersionInfo? MainAssembly,
	AssemblyVersionInfo? UnoUiAssembly,
	string? RuntimeVersion,
	string? RuntimeFrameworkName,
	string? GlobalizationMode,
	bool? LinkerEnabled,
	int? DebugLevel);

internal sealed record UnoConfig(Uri AssembliesPath, string? MainAssembly, string[]? Assemblies, string? Server, string? DotnetJsFilename);

internal sealed record UnoConfigFields(string? ManagePath, string? PackagePath, string? MainAssembly, string[]? Assemblies, string? DotnetJsFilename);

internal sealed record DotnetConfig(
	string? MainAssemblyName,
	string? GlobalizationMode,
	string[] Assemblies,
	Uri? AssembliesPath,
	int? DebugLevel,
	bool? LinkerEnabled,
	string? SourceUrl);
