using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using HtmlAgilityPack;
using Mono.Cecil;
using Uno.Wasm.WebCIL;

[assembly: InternalsVisibleTo("Uno.Wasm.VersionChecker.UnitTests")]

namespace Uno.VersionChecker;

public sealed class VersionCheckService(HttpClient httpClient) : IDisposable
{
	private static readonly Regex CommitRegex = new(
		@"[^\w]([0-9a-f]{40})([^\w]|$)",
		RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

	public async Task<VersionCheckReport> InspectAsync(VersionCheckTarget target, CancellationToken cancellationToken = default)
	{
		var doc = await LoadDocumentAsync(target.SiteUri, cancellationToken);
		var unoConfigUrl = await LocateUnoConfigAsync(doc, target.SiteUri, cancellationToken);
		UnoConfig? unoConfig = null;

		if (unoConfigUrl is not null)
		{
			unoConfig = await GetUnoConfigAsync(unoConfigUrl, target.SiteUri, cancellationToken);
		}

		var dotnetConfig = await GetPreferredDotnetConfigAsync(target.SiteUri, unoConfig, cancellationToken);
		var reportAssemblies = await ReadAssembliesAsync(target.SiteUri, unoConfig, dotnetConfig, cancellationToken);

		var mainAssemblyName = unoConfig?.MainAssembly ?? dotnetConfig?.MainAssemblyName;
		var dotnetMainName = Path.GetFileNameWithoutExtension(dotnetConfig?.MainAssemblyName);

		var mainAssembly = reportAssemblies.FirstOrDefault(assembly =>
			assembly.Name == mainAssemblyName || assembly.Name == dotnetMainName);
		var unoUiAssembly = reportAssemblies.FirstOrDefault(assembly => assembly.Name == "Uno.UI");
		var runtimeAssembly = reportAssemblies.FirstOrDefault(assembly =>
			assembly.Name is "System.Private.CoreLib" or "mscorlib" or "netstandard");

		return new VersionCheckReport(
			target,
			unoConfig?.Server,
			unoConfigUrl?.ToString(),
			dotnetConfig?.SourceUrl,
			dotnetConfig?.SourceUrl is null ? null : Path.GetFileName(dotnetConfig.SourceUrl),
			mainAssemblyName,
			reportAssemblies,
			mainAssembly,
			unoUiAssembly,
			runtimeAssembly?.Version,
			runtimeAssembly?.TargetFramework,
			dotnetConfig?.GlobalizationMode,
			dotnetConfig?.LinkerEnabled,
			dotnetConfig?.DebugLevel);
	}

	private async Task<HtmlDocument> LoadDocumentAsync(Uri siteUri, CancellationToken cancellationToken)
	{
		using var response = await httpClient.GetAsync(siteUri, cancellationToken);
		response.EnsureSuccessStatusCode();

		var content = await response.Content.ReadAsStringAsync(cancellationToken);
		var document = new HtmlDocument();
		document.LoadHtml(content);
		return document;
	}

	private async Task<Uri?> LocateUnoConfigAsync(HtmlDocument doc, Uri siteUri, CancellationToken cancellationToken)
	{
		var files = doc.DocumentNode
			.SelectNodes("//script")
			?.Select(scriptElement => scriptElement.GetAttributeValue("src", string.Empty))
			.Where(src => !string.IsNullOrWhiteSpace(src))
			.Select(src => new Uri(src, UriKind.RelativeOrAbsolute))
			.Where(uri => !uri.IsAbsoluteUri)
			.Select(uri => new Uri(siteUri, uri))
			.ToArray();

		var unoConfigPath = files?
			.FirstOrDefault(uri => uri.GetLeftPart(UriPartial.Path).EndsWith("uno-config.js", StringComparison.OrdinalIgnoreCase))
			?? files?.FirstOrDefault(uri => uri.GetLeftPart(UriPartial.Path).EndsWith("uno-bootstrap.js", StringComparison.OrdinalIgnoreCase));

		if (unoConfigPath is null)
		{
			var embeddedJsUri = new Uri(siteUri, "embedded.js");
			using var embeddedResponse = await httpClient.GetAsync(embeddedJsUri, cancellationToken);
			if (embeddedResponse.IsSuccessStatusCode)
			{
				var content = await embeddedResponse.Content.ReadAsStringAsync(cancellationToken);
				if (Regex.Match(content, @"const\spackage\s?=\s?""(?<package>package_\w+)"";") is { Success: true } match)
				{
					return new Uri(siteUri, match.Groups["package"].Value + "/uno-config.js");
				}
			}

			return null;
		}

		if (unoConfigPath.GetLeftPart(UriPartial.Path).EndsWith("uno-bootstrap.js", StringComparison.OrdinalIgnoreCase))
		{
			return new Uri(unoConfigPath.OriginalString.Replace("uno-bootstrap.js", "uno-config.js", StringComparison.OrdinalIgnoreCase));
		}

		return unoConfigPath;
	}

	private async Task<UnoConfig> GetUnoConfigAsync(Uri uri, Uri siteUri, CancellationToken cancellationToken)
	{
		using var response = await httpClient.GetAsync(uri, cancellationToken);
		response.EnsureSuccessStatusCode();

		var content = await response.Content.ReadAsStringAsync(cancellationToken);
		var server = response.Headers.Server?.ToString();

		var fields = ParseUnoConfigFields(content);
		var assembliesPath = fields.PackagePath is not null && fields.ManagePath is not null
			? new Uri(new Uri(siteUri, fields.PackagePath + "/"), fields.ManagePath + "/")
			: siteUri;

		return new UnoConfig(assembliesPath, fields.MainAssembly, fields.Assemblies, server, fields.DotnetJsFilename);
	}

	private async Task<DotnetConfig?> GetPreferredDotnetConfigAsync(Uri siteUri, UnoConfig? unoConfig, CancellationToken cancellationToken)
	{
		if (!string.IsNullOrWhiteSpace(unoConfig?.DotnetJsFilename))
		{
			var config = await GetDotnetConfigFromDotnetJsAsync(siteUri, unoConfig.DotnetJsFilename!, cancellationToken);
			if (config is not null)
			{
				return config;
			}
		}

		var defaultConfig = await GetDotnetConfigFromDotnetJsAsync(siteUri, "dotnet.js", cancellationToken);
		if (defaultConfig is not null)
		{
			return defaultConfig;
		}

		var dotnetConfigPath = await GetDotnetConfigPathAsync(siteUri, cancellationToken);
		return await GetDotnetConfigAsync(dotnetConfigPath, siteUri, cancellationToken);
	}

	private async Task<ImmutableArray<AssemblyVersionInfo>> ReadAssembliesAsync(
		Uri siteUri,
		UnoConfig? unoConfig,
		DotnetConfig? dotnetConfig,
		CancellationToken cancellationToken)
	{
		if (unoConfig?.Assemblies is { Length: > 0 })
		{
			return await ReadAssembliesAsync(unoConfig.AssembliesPath, unoConfig.Assemblies, cancellationToken);
		}

		if (dotnetConfig?.AssembliesPath is not null && dotnetConfig.Assemblies is { Length: > 0 })
		{
			return await ReadAssembliesAsync(dotnetConfig.AssembliesPath, dotnetConfig.Assemblies, cancellationToken);
		}

		throw new InvalidOperationException($"No assemblies were found for '{siteUri}'.");
	}

	private async Task<ImmutableArray<AssemblyVersionInfo>> ReadAssembliesAsync(
		Uri basePath,
		IEnumerable<string> assemblies,
		CancellationToken cancellationToken)
	{
		var tasks = assemblies.Select(a => GetAssemblyDetailsAsync(new Uri(basePath, a), cancellationToken)).ToArray();
		var allAssemblies = await Task.WhenAll(tasks);
		return allAssemblies
			.Where(x => x?.Name is not null)
			.Select(x => x!)
			.OrderBy(x => x.Name, StringComparer.Ordinal)
			.ToImmutableArray();
	}

	private async Task<Uri?> GetDotnetConfigPathAsync(Uri siteUri, CancellationToken cancellationToken)
	{
		if (await GetFileAsync(new Uri(siteUri, "_framework/blazor.boot.json"), cancellationToken) is { } frameworkFile)
		{
			return frameworkFile;
		}

		return await GetFileAsync(new Uri(siteUri, "blazor.boot.json"), cancellationToken);
	}

	private async Task<Uri?> GetFileAsync(Uri dotnetConfig, CancellationToken cancellationToken)
	{
		using var response = await httpClient.GetAsync(dotnetConfig, cancellationToken);
		if (!response.IsSuccessStatusCode)
		{
			return null;
		}

		var content = await response.Content.ReadAsStringAsync(cancellationToken);
		return content.StartsWith("{", StringComparison.Ordinal) ? dotnetConfig : null;
	}

	private async Task<DotnetConfig?> GetDotnetConfigAsync(Uri? dotnetConfigPath, Uri siteUri, CancellationToken cancellationToken)
	{
		if (dotnetConfigPath is null)
		{
			return null;
		}

		using var response = await httpClient.GetAsync(dotnetConfigPath, cancellationToken);
		response.EnsureSuccessStatusCode();

		await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
		using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

		var config = ParseDotnetConfig(json.RootElement);
		var assembliesPath = new Uri(siteUri, dotnetConfigPath.OriginalString.Contains("_framework", StringComparison.Ordinal) ? "_framework/" : string.Empty);
		return config with { AssembliesPath = assembliesPath, SourceUrl = dotnetConfigPath.ToString() };
	}

	internal static void ExtractAssembliesFromResources(JsonElement resources, string propertyName, List<string> assemblies)
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
			using var json = JsonDocument.Parse(match.Groups[1].Value);
			return ParseDotnetConfig(json.RootElement);
		}
		catch (JsonException)
		{
			return null;
		}
	}

	internal static UnoConfigFields ParseUnoConfigFields(string configContent)
	{
		string? managePath = null;
		string? packagePath = null;
		string? mainAssembly = null;
		string[]? assemblies = null;
		string? dotnetJsFilename = null;

		using var reader = new StringReader(configContent);
		while (reader.ReadLine() is { } line)
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

			if (managePath is not null && packagePath is not null && mainAssembly is not null && assemblies is not null)
			{
				break;
			}
		}

		return new UnoConfigFields(managePath, packagePath, mainAssembly, assemblies, dotnetJsFilename);
	}

	private async Task<DotnetConfig?> GetDotnetConfigFromDotnetJsAsync(Uri siteUri, string dotnetJsFilename, CancellationToken cancellationToken)
	{
		var dotnetJsUrl = new Uri(siteUri, $"_framework/{dotnetJsFilename}");

		try
		{
			using var response = await httpClient.GetAsync(dotnetJsUrl, cancellationToken);
			if (!response.IsSuccessStatusCode)
			{
				return null;
			}

			var content = await response.Content.ReadAsStringAsync(cancellationToken);
			var config = ExtractBootConfigFromScript(content);
			return config is null
				? null
				: config with
				{
					AssembliesPath = new Uri(siteUri, "_framework/"),
					SourceUrl = dotnetJsUrl.ToString()
				};
		}
		catch (HttpRequestException)
		{
			return null;
		}
	}

	private static DotnetConfig ParseDotnetConfig(JsonElement root)
	{
		string? mainAssemblyName = root.TryGetProperty("mainAssemblyName", out var mainProp)
			? mainProp.GetString()
			: null;
		string? globalizationMode = root.TryGetProperty("globalizationMode", out var globProp)
			? globProp.GetString()
			: null;
		int? debugLevel = root.TryGetProperty("debugLevel", out var dbgProp)
			? dbgProp.GetInt32()
			: null;
		bool? linkerEnabled = root.TryGetProperty("linkerEnabled", out var linkProp)
			? linkProp.GetBoolean()
			: null;

		var assemblies = new List<string>();
		if (root.TryGetProperty("resources", out var resources))
		{
			ExtractAssembliesFromResources(resources, "coreAssembly", assemblies);
			ExtractAssembliesFromResources(resources, "assembly", assemblies);
		}

		return new DotnetConfig(mainAssemblyName, globalizationMode, assemblies.ToArray(), null, debugLevel, linkerEnabled, null);
	}

	private async Task<AssemblyVersionInfo?> GetAssemblyDetailsAsync(Uri uri, CancellationToken cancellationToken)
	{
		using var request = new HttpRequestMessage(HttpMethod.Get, uri);
		using var response = await httpClient.SendAsync(request, cancellationToken);
		if (!response.IsSuccessStatusCode)
		{
			return null;
		}

		await using var httpStream = await response.Content.ReadAsStreamAsync(cancellationToken);

		try
		{
			using var stream = new MemoryStream();
			await httpStream.CopyToAsync(stream, cancellationToken);
			stream.Position = 0;

			var details = await ReadPeAsync(stream, cancellationToken);
			stream.Position = 0;
			details ??= ReadWebcil(stream);

			return details;
		}
		catch (Exception)
		{
			return null;
		}
	}

	private static AssemblyVersionInfo? ReadWebcil(MemoryStream stream)
	{
		var assemblyBytes = WebcilConverterUtil.ConvertFromWebcil(stream);
		return ParseAssemblyMetadata(new MemoryStream(assemblyBytes));
	}

	private static async Task<AssemblyVersionInfo?> ReadPeAsync(MemoryStream stream, CancellationToken cancellationToken)
	{
		var header = new byte[2];
		if (await stream.ReadAsync(header.AsMemory(0, 2), cancellationToken) != 2)
		{
			return null;
		}

		if (header[0] != 'M' || header[1] != 'Z')
		{
			return null;
		}

		stream.Position = 0;
		return ParseAssemblyMetadata(stream);
	}

	private static AssemblyVersionInfo? ParseAssemblyMetadata(MemoryStream stream)
	{
		var assembly = AssemblyDefinition.ReadAssembly(stream);
		var attributes = assembly.CustomAttributes.ToArray();

		var name = assembly.Name.Name;
		var version = assembly.Name.Version.ToString();
		var fileVersion = string.Empty;
		var targetFramework = string.Empty;
		var commit = default(string);
		var configuration = string.Empty;

		foreach (var attribute in attributes)
		{
			switch (attribute.AttributeType.Name)
			{
				case "AssemblyInformationalVersionAttribute":
					var versionStr = attribute.ConstructorArguments[0].Value?.ToString() ?? string.Empty;
					version = versionStr.Split('+').FirstOrDefault() ?? string.Empty;

					if (CommitRegex.Match(versionStr) is { Success: true } match)
					{
						commit = match.Groups[1].Value;
					}
					break;
				case "AssemblyFileVersionAttribute":
					fileVersion = attribute.ConstructorArguments[0].Value?.ToString() ?? string.Empty;
					break;
				case "TargetFrameworkAttribute":
					targetFramework = attribute.ConstructorArguments[0].Value?.ToString() ?? string.Empty;
					break;
				case "AssemblyConfigurationAttribute":
					configuration = attribute.ConstructorArguments[0].Value?.ToString() ?? string.Empty;
					break;
			}
		}

		if (attributes.Length == 0)
		{
			targetFramework = "WASM AOT";
		}

		return new AssemblyVersionInfo(name, version, fileVersion, configuration, targetFramework, commit);
	}

	public void Dispose() => httpClient.Dispose();
}
