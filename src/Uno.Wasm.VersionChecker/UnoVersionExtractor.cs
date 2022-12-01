using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Colorful;
using ConsoleTables;
using HtmlAgilityPack;
using Mono.Cecil;

using Console = Colorful.Console;

namespace Uno.VersionChecker;

public sealed class UnoVersionExtractor : IDisposable
{
	private readonly Uri _siteUri;
	private readonly HttpClient _httpClient = new HttpClient();

	private ImmutableArray<(string name, string version, string fileVersion, string configuration, string targetFramework, string? commit)> _assemblies;
	private (Uri assembliesPath, string? mainAssembly, string[]? assemblies, string? server) _config;
	private (string name, string version, string fileVersion, string configuration, string targetFramework, string? commit) _mainAssemblyDetails;
	private (string? version, string? name) _framework;
	private bool _isAot;
	
	public UnoVersionExtractor(Uri siteUri)
	{
		_siteUri = siteUri;
	}

	private void WriteTable(ConsoleTable table)
	{
		var writer = new StringWriter();
		table.Options.OutputTo = writer;
		table.Write(Format.Minimal);

		var alternator = new ColorAlternatorFactory().GetAlternator(1, Color.Aqua, Color.LightBlue);
		var isHeader = 2;

		foreach (var line in writer.ToString().Split(Environment.NewLine))
		{
			if (isHeader-- > 0)
			{
				Console.WriteLine(line, Color.White);
			}
			else
			{
				Console.WriteLineAlternating(line, alternator);
			}
		}
	}

	public async Task<int> Extract()
	{
		var web = new HtmlWeb();
		var doc = await web.LoadFromWebAsync(_siteUri, default, default, default);

		Console.WriteLine("Trying to find Uno bootstrapper configuration...", Color.Gray);

		var files = doc?.DocumentNode
			.SelectNodes("//script")
			.Select(scriptElement => scriptElement.GetAttributeValue("src", ""))
			.Where(src => !string.IsNullOrWhiteSpace(src))
			.Select(src => new Uri(src, UriKind.RelativeOrAbsolute))
			.Where(uri => !uri.IsAbsoluteUri)
			.Select(uri => new Uri(_siteUri, uri));

		var unoConfigPath = files
			?.FirstOrDefault(uri =>
				uri.GetLeftPart(UriPartial.Path).EndsWith("uno-config.js", StringComparison.OrdinalIgnoreCase));

		unoConfigPath??= files
			?.FirstOrDefault(uri =>
				uri.GetLeftPart(UriPartial.Path).EndsWith("uno-bootstrap.js", StringComparison.OrdinalIgnoreCase));

		if (unoConfigPath is null)
		{
			using var http = new HttpClient();
			var embeddedjs = new Uri(_siteUri, "embedded.js");
			var embeddedResponse = await http.GetAsync(embeddedjs);
			if (embeddedResponse.IsSuccessStatusCode)
			{
				var content = await embeddedResponse.Content.ReadAsStringAsync(default);
				if (Regex.Match(content, @"const\spackage\s?=\s?""(?<package>package_\w+)"";") is { Success : true } match)
				{
					var package = match.Groups["package"].Value + "/uno-config.js";
					unoConfigPath = new Uri(_siteUri, package);
				}
			}
		}
		else
		{
			if (unoConfigPath.GetLeftPart(UriPartial.Path).EndsWith("uno-bootstrap.js", StringComparison.OrdinalIgnoreCase))
			{
				unoConfigPath = new Uri(unoConfigPath.OriginalString.Replace("uno-bootstrap.js", "uno-config.js"));
			}
		}

		if (unoConfigPath is null)
		{
			Console.WriteLine("No Uno / Uno.UI application found.", Color.Red);
			return 2;
		}

		Console.WriteLine("Application found.", Color.LightGreen);

		Console.WriteLineFormatted("Configuration url is {0}.", Color.Gray, new Formatter(unoConfigPath, Color.Aqua));

		_config = await GetConfig(unoConfigPath);

		if (_config.server is { })
		{
			Console.WriteLineFormatted(
				"Server is {0}",
				Color.Gray,
				new Formatter(_config.server, Color.Aqua));
		}

		Console.WriteLineFormatted("Starting assembly is {0}.", Color.Gray,
			new Formatter(_config.mainAssembly, Color.Aqua));

		if (_config.assemblies is null || _config.assemblies is { Length: 0 })
		{
			Console.WriteLine("No assemblies found. That's odd.", Color.Red);
			return 1;
		}

		Console.WriteLineFormatted("{0} assemblies found. Downloading assemblies to read metadata...", Color.Gray,
			new Formatter(_config.assemblies.Length, Color.Aqua));

		var tasks = _config.assemblies
			.Select(a => GetAssemblyDetails(new Uri(_config.assembliesPath, a)))
			.ToArray();

		_assemblies = (await Task.WhenAll(tasks))
			.OrderBy(d => d.name)
			.ToImmutableArray();

		_mainAssemblyDetails = default;
		foreach (var assemblyDetail in _assemblies)
		{
			if (assemblyDetail.name == _config.mainAssembly)
			{
				_mainAssemblyDetails = assemblyDetail;
			}
			else if (assemblyDetail.name is "mscorlib" or "netstandard")
			{
				_framework = (assemblyDetail.version, assemblyDetail.targetFramework);
			}
			else if (assemblyDetail.name == "aot-instances")
			{
				_isAot = true;
			}
		}

		return 0;
	}

	public void OutputResults()
	{
		var table = new ConsoleTable("Name", "Version", "File Version", "Build", "Framework", "Commit");

		foreach (var assemblyDetail in _assemblies)
		{
			table.AddRow(
				assemblyDetail.name,
				assemblyDetail.version,
				assemblyDetail.fileVersion,
				assemblyDetail.configuration,
				assemblyDetail.targetFramework,
				assemblyDetail.commit);
		}

		Console.WriteLine();

		WriteTable(table);

		if (_mainAssemblyDetails.name is { })
		{
			var configuration = _mainAssemblyDetails.configuration;

			Console.WriteLineFormatted("{0} version is {1} ({2})", Color.Gray,
				new Formatter(_mainAssemblyDetails.name, Color.Aqua),
				new Formatter(_mainAssemblyDetails.version, Color.Aqua),
				new Formatter(configuration, configuration is "Release" ? Color.Aqua : Color.Orange));
		}

		var uno = _assemblies.FirstOrDefault(d => d.name is "Uno.UI");
		if (uno is { name: { } })
		{
			Console.WriteLineFormatted("Uno.UI version is {0}", Color.Gray, new Formatter(uno.version, Color.Aqua));
		}
		else
		{
			Console.WriteLine(
				"Unable to identify the version of Uno.UI on this application. Maybe this application is only using the Uno bootstrapper.",
				Color.Orange);
		}

		if (_framework.name is { })
		{
			Console.WriteLineFormatted(
				"Runtime is {0} version {1}",
				Color.Gray,
				new Formatter(_framework.name, Color.Aqua),
				new Formatter(_framework.version, Color.Aqua));
		}
		else
		{
			Console.WriteLine(
				"Unable to identify the runtime.",
				Color.Orange);
		}
	}

	private async Task<(Uri assembliesPath, string? mainAssembly, string[]? assemblies, string? server)> GetConfig(Uri uri)
	{
		using var response = await _httpClient.GetAsync(uri);
		response.EnsureSuccessStatusCode();

		await using var stream = await response.Content.ReadAsStreamAsync();
		using var reader = new StreamReader(stream);

		string? managePath = default;
		string? packagePath = default;
		string? mainAssembly = default;
		string[]? assemblies = default;
		var server = response.Headers.Server?.ToString();

		while (!reader.EndOfStream)
		{
			var line = await reader.ReadLineAsync();
			if (string.IsNullOrWhiteSpace(line))
			{
				continue;
			}

			var parts = line.Split(new[] { '=' }, 2);
			if (parts.Length != 2)
			{
				continue;
			}

			var field = parts[0].Trim().ToLowerInvariant();
			var value = parts[1].Trim().TrimEnd(';');

			switch (field)
			{
				case "config.uno_remote_managedpath":
					managePath = JsonSerializer.Deserialize<string>(value);
					break;
				case "config.uno_app_base":
					packagePath = JsonSerializer.Deserialize<string>(value);
					break;
				case "config.assemblies_with_size":
					assemblies = JsonSerializer.Deserialize<Dictionary<string, long>>(value)?.Keys.ToArray();
					break;
				case "config.uno_main":
					mainAssembly = JsonSerializer.Deserialize<string>(value)?.Split(']', 2)[0].TrimStart('[');
					break;
			}

			if (managePath is { } && packagePath is { } && mainAssembly is { } && assemblies is { })
			{
				break;
			}
		}

		var assembliesPath = new Uri(new Uri(_siteUri, packagePath + "/"), managePath + "/");

		return (assembliesPath, mainAssembly, assemblies, server);
	}

	private static readonly Regex COMMIT_REGEX = new Regex(
		@"[^\w]([0-9a-f]{40})([^\w]|$)",
		RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);


	private async Task<(string name, string version, string fileVersion, string configuration, string targetFramework, string? commit)> GetAssemblyDetails(Uri uri)
	{
		await using var httpStream = await _httpClient.GetStreamAsync(uri);

		var stream = new MemoryStream();
		await httpStream.CopyToAsync(stream);
		stream.Position = 0;

		var assembly = AssemblyDefinition.ReadAssembly(stream);

		var attributes = assembly.CustomAttributes.ToArray();

		var name = assembly.Name.Name;
		var version = assembly.Name.Version.ToString();
		var fileVersion = "";
		var targetFramework = "";
		var commit = default(string?);
		var configuration = "";

		foreach (var attribute in attributes)
		{
			switch (attribute.AttributeType.Name)
			{
				case "AssemblyInformationalVersionAttribute":
					var versionStr = attribute.ConstructorArguments[0].Value?.ToString() ?? "";
					version = versionStr.Split('+').FirstOrDefault() ?? "";

					if (COMMIT_REGEX.Match(versionStr) is { Success: true } m)
					{
						commit = m.Groups[1].Value;
					}

					break;
				case "AssemblyFileVersionAttribute":
					fileVersion = attribute.ConstructorArguments[0].Value?.ToString() ?? "";
					break;
				case "TargetFrameworkAttribute":
					targetFramework = attribute.ConstructorArguments[0].Value?.ToString() ?? "";
					break;
				case "AssemblyConfigurationAttribute":
					configuration = attribute.ConstructorArguments[0].Value?.ToString() ?? "";
					break;
			}
		}

		if (attributes.Length == 0)
		{
			targetFramework = "WASM AOT";
		}

		return (name, version, fileVersion, configuration, targetFramework, commit);
	}


	public void Dispose() => _httpClient.Dispose();
}
