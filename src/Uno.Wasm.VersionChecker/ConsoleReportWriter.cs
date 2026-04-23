using Colorful;
using ConsoleTables;
using System;
using System.Drawing;
using System.IO;

namespace Uno.VersionChecker;

using Console = Colorful.Console;

internal static class ConsoleReportWriter
{
	public static void WriteBanner(string? version)
	{
		Console.WriteLineFormatted("Uno Version Checker v{0}.", Color.Gray, new Formatter(version ?? "unknown", Color.Aqua));
	}

	public static void WriteUsage(string moduleName)
	{
		Console.WriteLine($"Usage: {moduleName} [application hostname or url]", Color.Yellow);
		Console.WriteLine("Default scheme is https if not specified.", Color.Yellow);
	}

	public static void WriteCheckingTarget(VersionCheckTarget target)
	{
		Console.WriteLineFormatted("Checking website at address {0}.", Color.Gray, new Formatter(target.SiteUri, Color.Aqua));
	}

	public static void WriteSuccessPreamble(VersionCheckReport report)
	{
		Console.WriteLine("Trying to find App configuration...", Color.Gray);
		Console.WriteLine("Application found.", Color.LightGreen);

		if (!string.IsNullOrWhiteSpace(report.UnoConfigUrl))
		{
			Console.WriteLineFormatted("Uno configuration url is {0}.", Color.Gray, new Formatter(report.UnoConfigUrl, Color.Aqua));
		}

		if (!string.IsNullOrWhiteSpace(report.Server))
		{
			Console.WriteLineFormatted("Server is {0}", Color.Gray, new Formatter(report.Server, Color.Aqua));
		}

		if (!string.IsNullOrWhiteSpace(report.BootConfigSource))
		{
			Console.WriteLineFormatted("Boot configuration extracted from {0}.", Color.Gray, new Formatter(report.BootConfigSource, Color.Aqua));
		}
		else if (!string.IsNullOrWhiteSpace(report.DotnetConfigUrl))
		{
			Console.WriteLineFormatted("Dotnet configuration url is {0}.", Color.Gray, new Formatter(report.DotnetConfigUrl, Color.Aqua));
		}

		if (!string.IsNullOrWhiteSpace(report.MainAssemblyName))
		{
			Console.WriteLineFormatted("Starting assembly is {0}.", Color.Gray, new Formatter(report.MainAssemblyName, Color.Aqua));
		}

		Console.WriteLineFormatted(
			"Trying to download {0} files to find assemblies. Downloading them to read metadata...",
			Color.Gray,
			new Formatter(report.Assemblies.Length, Color.Aqua));
	}

	public static void WriteResults(VersionCheckReport report)
	{
		var table = new ConsoleTable("Name", "Version", "File Version", "Build", "Framework", "Commit");

		foreach (var assembly in report.Assemblies)
		{
			table.AddRow(
				assembly.Name,
				assembly.Version,
				assembly.FileVersion,
				assembly.Configuration,
				assembly.TargetFramework,
				assembly.Commit);
		}

		Console.WriteLine();
		Console.WriteLineFormatted("{0} assemblies successfully downloaded.", Color.Gray, new Formatter(report.Assemblies.Length, Color.Aqua));
		WriteTable(table);

		if (report.MainAssembly is { Name: { } mainName } mainAssembly)
		{
			Console.WriteLineFormatted(
				"{0} version is {1} ({2})",
				Color.Gray,
				new Formatter(mainName, Color.Aqua),
				new Formatter(mainAssembly.Version, Color.Aqua),
				new Formatter(mainAssembly.Configuration, mainAssembly.Configuration is "Release" ? Color.Aqua : Color.Orange));
		}

		if (report.UnoUiAssembly is not null)
		{
			Console.WriteLineFormatted("Uno.UI version is {0}", Color.Gray, new Formatter(report.UnoUiAssembly.Version, Color.Aqua));
		}
		else
		{
			Console.WriteLine(
				"Unable to identify the version of Uno.UI on this application. Maybe this application is only using the Uno bootstrapper.",
				Color.Orange);
		}

		if (!string.IsNullOrWhiteSpace(report.RuntimeFrameworkName))
		{
			Console.WriteLineFormatted(
				"Runtime is {0} version {1}",
				Color.Gray,
				new Formatter(report.RuntimeFrameworkName, Color.Aqua),
				new Formatter(report.RuntimeVersion, Color.Aqua));
		}
		else
		{
			Console.WriteLine("Unable to identify the runtime.", Color.Orange);
		}

		if (!string.IsNullOrWhiteSpace(report.GlobalizationMode))
		{
			Console.WriteLineFormatted("Globalization mode is {0}", Color.Gray, new Formatter(report.GlobalizationMode, Color.Aqua));
		}

		if (report.LinkerEnabled is { } linkerEnabled)
		{
			Console.WriteLineFormatted("Linker is {0}", Color.Gray, new Formatter(linkerEnabled ? "enabled" : "disabled", Color.Aqua));
		}

		if (report.DebugLevel is { } debugLevel)
		{
			Console.WriteLineFormatted("Debug level is {0}", Color.Gray, new Formatter(debugLevel, Color.Aqua));
		}
	}

	private static void WriteTable(ConsoleTable table)
	{
		var writer = new StringWriter();
		table.Options.OutputTo = writer;
		table.Write(Format.Minimal);

		var alternator = new ColorAlternatorFactory().GetAlternator(1, Color.Aqua, Color.LightBlue);
		var headerLines = 2;

		foreach (var line in writer.ToString().Split(Environment.NewLine))
		{
			if (headerLines-- > 0)
			{
				Console.WriteLine(line, Color.White);
			}
			else
			{
				Console.WriteLineAlternating(line, alternator);
			}
		}
	}
}
