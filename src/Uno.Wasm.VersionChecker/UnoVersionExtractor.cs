using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel.DataAnnotations;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Colorful;
using ConsoleTables;
using HtmlAgilityPack;
using Mono.Cecil;
using Uno.Wasm.WebCIL;
using Console = Colorful.Console;

[assembly: InternalsVisibleTo("Uno.Wasm.VersionChecker.UnitTests")]

namespace Uno.VersionChecker;

public sealed class UnoVersionExtractor : IDisposable
{
	private readonly Uri _siteUri;
	private readonly HttpClient _httpClient = new HttpClient();

	internal record UnoConfig(Uri assembliesPath, string? mainAssembly, string[]? assemblies, string? server, string? dotnetJsFilename);
	internal record UnoConfigFields(string? managePath, string? packagePath, string? mainAssembly, string[]? assemblies, string? dotnetJsFilename);
	public record AssemblyDetail(string? name, string version, string fileVersion, string configuration, string targetFramework, string? commit);

	private ImmutableArray<AssemblyDetail> _assemblies;
	private UnoConfig? _unoConfig;
	private DotnetConfig? _dotnetConfig;
	private AssemblyDetail? _mainAssemblyDetails;
	private (string? version, string? name) _framework;

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

		Console.WriteLine("Trying to find App configuration...", Color.Gray);

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

		unoConfigPath ??= files
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
				if (Regex.Match(content, @"const\spackage\s?=\s?""(?<package>package_\w+)"";") is { Success: true } match)
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

		Console.WriteLine("Application found.", Color.LightGreen);

		if (unoConfigPath is not null)
		{
			Console.WriteLineFormatted("Uno configuration url is {0}.", Color.Gray, new Formatter(unoConfigPath, Color.Aqua));
			_unoConfig = await GetUnoConfig(unoConfigPath);
		}

		if (_unoConfig?.server is { Length: > 0 })
		{
			Console.WriteLineFormatted(
				"Server is {0}",
				Color.Gray,
				new Formatter(_unoConfig.server, Color.Aqua));
		}

		// Try modern path: embedded boot config in dotnet.js
		if (_unoConfig?.dotnetJsFilename is { Length: > 0 } dotnetJsFilename)
		{
			_dotnetConfig = await GetDotnetConfigFromDotnetJs(dotnetJsFilename);
		}

		// Fallback: try default dotnet.js filename
		_dotnetConfig ??= await GetDotnetConfigFromDotnetJs("dotnet.js");

		// Fallback: try blazor.boot.json for older apps
		if (_dotnetConfig is null)
		{
			var dotnetConfigPath = await GetDotnetConfigPath();
			_dotnetConfig = await GetDotnetConfig(dotnetConfigPath);

			if (_dotnetConfig?.mainAssemblyName is not null)
			{
				Console.WriteLineFormatted("Dotnet configuration url is {0}.", Color.Gray, new Formatter(dotnetConfigPath, Color.Aqua));
			}
		}

		if (_unoConfig?.mainAssembly is { Length: > 0 })
		{
			Console.WriteLineFormatted("Starting assembly is {0}.", Color.Gray,
				new Formatter(_unoConfig.mainAssembly, Color.Aqua));
		}

		if (_dotnetConfig?.mainAssemblyName is { Length: > 0 })
		{
			Console.WriteLineFormatted("Starting assembly is {0}.", Color.Gray,
				new Formatter(_dotnetConfig.mainAssemblyName, Color.Aqua));
		}

		if (
			(_unoConfig?.assemblies is null || _unoConfig.assemblies is { Length: 0 })
			&& (_dotnetConfig?.assemblies is null || _dotnetConfig.assemblies is { Length: 0 }))
		{
			Console.WriteLine("No assemblies found.", Color.Red);
			return 1;
		}

		if(_unoConfig?.assemblies is { Length: > 0 })
		{
			await DumpAssemblies(_unoConfig.assembliesPath, _unoConfig.assemblies);
		}
		else if(_dotnetConfig?.assembliesPath is not null)
		{
			await DumpAssemblies(_dotnetConfig.assembliesPath, _dotnetConfig.assemblies);
		}
		else
		{
			Console.WriteLine("Invalid configuration", Color.Red);
			return 1;
		}

		return 0;
	}

	private async Task DumpAssemblies(Uri basePath, string[] assemblies)
	{
		Console.WriteLineFormatted(
			"Trying to download {0} files to find assemblies. Downloading them to read metadata...",
			Color.Gray,
			new Formatter(assemblies.Length, Color.Aqua));

		var tasks = assemblies
					.Select(a => GetAssemblyDetails(new Uri(basePath, a)))
					.ToArray();

		_assemblies = (await Task.WhenAll(tasks))
			.Where(x => x?.name is not null)
			.OrderBy(d => d?.name)
			.ToImmutableArray()!;

		_mainAssemblyDetails = null;

		var dotnetMainName = Path.GetFileNameWithoutExtension(_dotnetConfig?.mainAssemblyName);

		foreach (var assemblyDetail in _assemblies)
		{
			if (assemblyDetail.name == _unoConfig?.mainAssembly
				|| assemblyDetail.name == dotnetMainName)
			{
				_mainAssemblyDetails = assemblyDetail;
			}
			else if (assemblyDetail.name is "mscorlib" or "netstandard")
			{
				_framework = (assemblyDetail.version, assemblyDetail.targetFramework);
			}
			else if (assemblyDetail.name is "System.Private.CoreLib")
			{
				_framework = (assemblyDetail.version, assemblyDetail.targetFramework);
			}
		}
	}

	private async Task<Uri?> GetDotnetConfigPath()
	{
		using var http = new HttpClient();

		if (await GetFile(http, new(_siteUri, "_framework/blazor.boot.json")) is { } frameworkFile)
		{
			return frameworkFile;
		}
		else if (await GetFile(http, new(_siteUri, "blazor.boot.json")) is { } baseFile)
		{
			return baseFile;
		}

		return null;
	}

	private static async Task<Uri?> GetFile(HttpClient http, Uri dotnetConfig)
	{
		var embeddedResponse = await http.GetAsync(dotnetConfig);

		if (embeddedResponse.IsSuccessStatusCode)
		{
			var response = await embeddedResponse.Content.ReadAsStringAsync();

			if (response.StartsWith("{"))
			{
				return dotnetConfig;
			}
		}

		return null;
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

		Console.WriteLineFormatted("{0} assemblies successfully downloaded.", Color.Gray, new Formatter(_assemblies.Length, Color.Aqua));

		WriteTable(table);

		if (_mainAssemblyDetails?.name is { })
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

		if (_dotnetConfig?.globalizationMode is { Length: > 0 } globMode)
		{
			Console.WriteLineFormatted("Globalization mode is {0}", Color.Gray,
				new Formatter(globMode, Color.Aqua));
		}

		if (_dotnetConfig?.linkerEnabled is { } linker)
		{
			Console.WriteLineFormatted("Linker is {0}", Color.Gray,
				new Formatter(linker ? "enabled" : "disabled", Color.Aqua));
		}

		if (_dotnetConfig?.debugLevel is { } dbgLevel)
		{
			Console.WriteLineFormatted("Debug level is {0}", Color.Gray,
				new Formatter(dbgLevel, Color.Aqua));
		}
	}

	private async Task<DotnetConfig?> GetDotnetConfig(Uri? dotnetConfigPath)
	{
		if (dotnetConfigPath is null)
		{
			return null;
		}

		using var response = await _httpClient.GetAsync(dotnetConfigPath);
		response.EnsureSuccessStatusCode();

		await using var stream = await response.Content.ReadAsStreamAsync();

		using var json = await JsonDocument.ParseAsync(stream);

		var root = json.RootElement;

		string? mainAssemblyName = root.TryGetProperty("mainAssemblyName", out var mainProp)
			? mainProp.GetString() : null;
		string? globalizationMode = root.TryGetProperty("globalizationMode", out var globProp)
			? globProp.GetString() : null;
		int? debugLevel = root.TryGetProperty("debugLevel", out var dbgProp)
			? dbgProp.GetInt32() : null;
		bool? linkerEnabled = root.TryGetProperty("linkerEnabled", out var linkProp)
			? linkProp.GetBoolean() : null;

		var assemblies = new List<string>();

		if (root.TryGetProperty("resources", out var resources))
		{
			ExtractAssembliesFromResources(resources, "coreAssembly", assemblies);
			ExtractAssembliesFromResources(resources, "assembly", assemblies);
		}

		var assembliesPath = new Uri(_siteUri, dotnetConfigPath.OriginalString.Contains("_framework") ? "_framework/" : "");

		return new DotnetConfig(mainAssemblyName, globalizationMode, assemblies.ToArray(), assembliesPath, debugLevel, linkerEnabled);
	}

	private async Task<UnoConfig> GetUnoConfig(Uri uri)
	{
		using var response = await _httpClient.GetAsync(uri);
		response.EnsureSuccessStatusCode();

		var content = await response.Content.ReadAsStringAsync();
		var server = response.Headers.Server?.ToString();

		var fields = ParseUnoConfigFields(content);

		var assembliesPath = new Uri(new Uri(_siteUri, fields.packagePath + "/"), fields.managePath + "/");

		return new UnoConfig(assembliesPath, fields.mainAssembly, fields.assemblies, server, fields.dotnetJsFilename);
	}

	internal static void ExtractAssembliesFromResources(
		JsonElement resources, string propertyName, List<string> assemblies)
	{
		if (!resources.TryGetProperty(propertyName, out var element)
			|| element.ValueKind == JsonValueKind.Undefined)
		{
			return;
		}

		if (element.ValueKind == JsonValueKind.Array)
		{
			foreach (var entry in element.EnumerateArray())
			{
				if (entry.TryGetProperty("name", out var nameProp)
					&& nameProp.GetString() is { Length: > 0 } name)
				{
					assemblies.Add(name);
				}
			}
		}
		else if (element.ValueKind == JsonValueKind.Object)
		{
			foreach (var entry in element.EnumerateObject())
			{
				if (entry.Name is { Length: > 0 })
				{
					assemblies.Add(entry.Name);
				}
			}
		}
	}

	internal static DotnetConfig? ExtractBootConfigFromScript(string scriptContent)
	{
		var match = Regex.Match(scriptContent, @"/\*json-start\*/([\s\S]*?)/\*json-end\*/");
		if (!match.Success)
		{
			return null;
		}

		try
		{
			var jsonText = match.Groups[1].Value;
			using var json = JsonDocument.Parse(jsonText);
			var root = json.RootElement;

			string? mainAssemblyName = root.TryGetProperty("mainAssemblyName", out var mainProp)
				? mainProp.GetString() : null;
			string? globalizationMode = root.TryGetProperty("globalizationMode", out var globProp)
				? globProp.GetString() : null;
			int? debugLevel = root.TryGetProperty("debugLevel", out var dbgProp)
				? dbgProp.GetInt32() : null;
			bool? linkerEnabled = root.TryGetProperty("linkerEnabled", out var linkProp)
				? linkProp.GetBoolean() : null;

			var assemblies = new List<string>();
			if (root.TryGetProperty("resources", out var resources))
			{
				ExtractAssembliesFromResources(resources, "coreAssembly", assemblies);
				ExtractAssembliesFromResources(resources, "assembly", assemblies);
			}

			return new DotnetConfig(mainAssemblyName, globalizationMode, assemblies.ToArray(), null, debugLevel, linkerEnabled);
		}
		catch (JsonException)
		{
			return null;
		}
	}

	internal static UnoConfigFields ParseUnoConfigFields(string configContent)
	{
		string? managePath = default;
		string? packagePath = default;
		string? mainAssembly = default;
		string[]? assemblies = default;
		string? dotnetJsFilename = default;

		using var reader = new StringReader(configContent);
		string? line;

		while ((line = reader.ReadLine()) is not null)
		{
			var parts = line.Split(['='], 2);
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
				case "config.dotnet_js_filename":
					dotnetJsFilename = JsonSerializer.Deserialize<string>(value);
					break;
			}

			if (managePath is { } && packagePath is { } && mainAssembly is { } && assemblies is { } && dotnetJsFilename is { })
			{
				break;
			}
		}

		return new UnoConfigFields(managePath, packagePath, mainAssembly, assemblies, dotnetJsFilename);
	}

	private async Task<DotnetConfig?> GetDotnetConfigFromDotnetJs(string dotnetJsFilename)
	{
		var dotnetJsUrl = new Uri(_siteUri, $"_framework/{dotnetJsFilename}");

		try
		{
			using var response = await _httpClient.GetAsync(dotnetJsUrl);
			if (!response.IsSuccessStatusCode)
			{
				return null;
			}

			var content = await response.Content.ReadAsStringAsync();
			var config = ExtractBootConfigFromScript(content);

			if (config is null)
			{
				return null;
			}

			Console.WriteLineFormatted(
				"Boot configuration extracted from {0}.",
				Color.Gray,
				new Formatter(dotnetJsFilename, Color.Aqua));

			return config with { assembliesPath = new Uri(_siteUri, "_framework/") };
		}
		catch (HttpRequestException ex)
		{
			Console.WriteLineFormatted(
				"Failed to download {0}: {1}",
				Color.Yellow,
				new Formatter(dotnetJsUrl.ToString(), Color.Aqua),
				new Formatter(ex.Message, Color.Red));
			return null;
		}
	}

	private static readonly Regex COMMIT_REGEX = new Regex(
		@"[^\w]([0-9a-f]{40})([^\w]|$)",
		RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

	private async Task<AssemblyDetail?> GetAssemblyDetails(Uri uri)
	{
		// Create http response from uri
		using var request = new HttpRequestMessage(HttpMethod.Get, uri);
		using var response = await _httpClient.SendAsync(request);
		if (!response.IsSuccessStatusCode)
		{
			return default;
		}

		await using var httpStream = await response.Content.ReadAsStreamAsync();

		try
		{
			var stream = new MemoryStream();
			await httpStream.CopyToAsync(stream);
			stream.Position = 0;

			var details = await ReadPE(stream);

			stream.Position = 0;
			details ??= ReadWebCIL(stream);

			return details;
		}
		catch (Exception)
		{
			return null;
		}
	}

	private static AssemblyDetail? ReadWebCIL(MemoryStream stream)
	{
		var asmStream = WebcilConverterUtil.ConvertFromWebcil(stream);

		return ParseAssemblyMetadata(new MemoryStream(asmStream));
	}

	private static async Task<AssemblyDetail?> ReadPE(MemoryStream stream)
	{
		// Ensure it's a DLL file by reading the header
		var header = new byte[2];
		if (await stream.ReadAsync(header, 0, 2) != 2)
		{
			return null;
		}
		if (header[0] != 'M' || header[1] != 'Z')
		{
			return null;
		}

		// Reset the stream
		stream.Position = 0;

		return ParseAssemblyMetadata(stream);
	}

	private static AssemblyDetail? ParseAssemblyMetadata(MemoryStream stream)
	{
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

		return new(name, version, fileVersion, configuration, targetFramework, commit);
	}

	public void Dispose() => _httpClient.Dispose();
}

internal record DotnetConfig(string? mainAssemblyName, string? globalizationMode, string[] assemblies, Uri? assembliesPath, int? debugLevel, bool? linkerEnabled);
