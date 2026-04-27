using System;
using System.Buffers;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using HtmlAgilityPack;
using Mono.Cecil;
using Uno.Wasm.WebCIL;

namespace Uno.VersionChecker;

public sealed class VersionCheckService(HttpClient httpClient)
{
	private const int MaxAssemblyDownloadConcurrency = 8;
	private const int MaxTooManyRequestsRetries = 2;
	private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(2);
	private static readonly Regex CommitRegex = new(
		@"[^\w]([0-9a-f]{40})([^\w]|$)",
		RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant,
		RegexTimeout);
	private static readonly Regex EmbeddedPackageRegex = new(
		"""const\s+package\s?=\s?"(?<package>package_\w+)";""",
		RegexOptions.Compiled | RegexOptions.CultureInvariant,
		RegexTimeout);
	private static readonly Regex BootConfigRegex = new(
		@"/\*json-start\*/([\s\S]*?)/\*json-end\*/",
		RegexOptions.Compiled | RegexOptions.CultureInvariant,
		RegexTimeout);
	private static readonly string[] RuntimeAssemblyNames = ["System.Private.CoreLib", "mscorlib", "netstandard"];
	private static readonly TimeSpan[] TooManyRequestsBackoff = [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)];
	private static ReadOnlySpan<byte> Utf8Bom => [0xEF, 0xBB, 0xBF];

	public async Task<VersionCheckReport> InspectAsync(VersionCheckTarget target, CancellationToken cancellationToken = default)
	{
		var doc = await LoadDocumentAsync(target.SiteUri, cancellationToken);
		var unoConfigUrl = await LocateUnoConfigAsync(doc, target.SiteUri, cancellationToken);
		var unoConfig = unoConfigUrl is not null
			? await GetUnoConfigAsync(unoConfigUrl, target.SiteUri, cancellationToken)
			: null;

		var dotnetConfig = await GetPreferredDotnetConfigAsync(target.SiteUri, unoConfig, cancellationToken);
		var reportAssemblies = await ReadAssembliesAsync(target.SiteUri, unoConfig, dotnetConfig, cancellationToken);

		var mainAssemblyName = unoConfig?.MainAssembly ?? dotnetConfig?.MainAssemblyName;
		var dotnetMainName = Path.GetFileNameWithoutExtension(dotnetConfig?.MainAssemblyName);
		var mainAssembly = reportAssemblies.FirstOrDefault(assembly =>
			assembly.Name == mainAssemblyName || assembly.Name == dotnetMainName);
		var unoUiAssembly = reportAssemblies.FirstOrDefault(assembly => assembly.Name == "Uno.UI");
		var runtimeAssembly = reportAssemblies.FirstOrDefault(assembly =>
			assembly.Name is not null && RuntimeAssemblyNames.Contains(assembly.Name, StringComparer.Ordinal));

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
		var content = await GetStringAsync(siteUri, cancellationToken);
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
			.Select(src => TryResolveTrustedScriptUri(siteUri, src))
			.Where(uri => uri is not null)
			.Select(uri => uri!)
			.ToArray();

		var unoConfigPath = files?
			.FirstOrDefault(uri => uri.GetLeftPart(UriPartial.Path).EndsWith("uno-config.js", StringComparison.OrdinalIgnoreCase))
			?? files?.FirstOrDefault(uri => uri.GetLeftPart(UriPartial.Path).EndsWith("uno-bootstrap.js", StringComparison.OrdinalIgnoreCase));

		if (unoConfigPath is not null)
		{
			return unoConfigPath.GetLeftPart(UriPartial.Path).EndsWith("uno-bootstrap.js", StringComparison.OrdinalIgnoreCase)
				? new Uri(unoConfigPath.OriginalString.Replace("uno-bootstrap.js", "uno-config.js", StringComparison.OrdinalIgnoreCase))
				: unoConfigPath;
		}

		var embeddedJsUri = new Uri(siteUri, "embedded.js");
		using var embeddedResponse = await SendAsync(embeddedJsUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
		if (!embeddedResponse.IsSuccessStatusCode)
		{
			return null;
		}

		var content = await ReadContentAsStringAsync(embeddedResponse.Content, embeddedJsUri, cancellationToken);
		if (EmbeddedPackageRegex.Match(content) is { Success: true } match)
		{
			return VersionCheckNetworkPolicy.ResolveTrustedUri(siteUri, siteUri, match.Groups["package"].Value + "/uno-config.js", "embedded.js");
		}

		return null;
	}

	private static Uri? TryResolveTrustedScriptUri(Uri siteUri, string src)
	{
		try
		{
			return VersionCheckNetworkPolicy.ResolveTrustedUri(siteUri, siteUri, src, "script src");
		}
		catch (InvalidOperationException)
		{
			return null;
		}
		catch (UriFormatException)
		{
			return null;
		}
	}

	private async Task<UnoConfig> GetUnoConfigAsync(Uri uri, Uri siteUri, CancellationToken cancellationToken)
	{
		using var response = await SendAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
		response.EnsureSuccessStatusCode();

		var content = await ReadContentAsStringAsync(response.Content, uri, cancellationToken);
		var server = response.Headers.Server?.ToString();

		var fields = ParseUnoConfigFields(content);
		var assembliesPath = fields.PackagePath is not null && fields.ManagedPath is not null
			? VersionCheckNetworkPolicy.ResolveTrustedUri(
				siteUri,
				VersionCheckNetworkPolicy.ResolveTrustedUri(siteUri, siteUri, fields.PackagePath.TrimEnd('/') + "/", "uno_app_base"),
				fields.ManagedPath.TrimEnd('/') + "/",
				"uno_remote_managedpath")
			: siteUri;

		return new UnoConfig(assembliesPath, fields.MainAssembly, fields.Assemblies, server, fields.DotnetJsFilename);
	}

	private async Task<DotnetConfig?> GetPreferredDotnetConfigAsync(Uri siteUri, UnoConfig? unoConfig, CancellationToken cancellationToken)
	{
		var managedPath = unoConfig?.AssembliesPath;
		var configuredFilename = unoConfig?.DotnetJsFilename;
		if (!string.IsNullOrWhiteSpace(configuredFilename))
		{
			var configuredConfig = await GetDotnetConfigFromDotnetJsAsync(siteUri, configuredFilename, managedPath, cancellationToken);
			if (configuredConfig is not null)
			{
				return configuredConfig;
			}
		}

		var defaultConfig = await GetDotnetConfigFromDotnetJsAsync(siteUri, "dotnet.js", managedPath, cancellationToken);
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
		if (unoConfig is { Assemblies.IsDefaultOrEmpty: false })
		{
			return await ReadAssembliesAsync(siteUri, unoConfig.AssembliesPath, unoConfig.Assemblies, cancellationToken);
		}

		if (dotnetConfig?.AssembliesPath is not null && !dotnetConfig.Assemblies.IsDefaultOrEmpty)
		{
			return await ReadAssembliesAsync(siteUri, dotnetConfig.AssembliesPath, dotnetConfig.Assemblies, cancellationToken);
		}

		throw new InvalidOperationException($"No assemblies were found for '{siteUri}'.");
	}

	private async Task<ImmutableArray<AssemblyVersionInfo>> ReadAssembliesAsync(
		Uri siteUri,
		Uri basePath,
		IReadOnlyList<string> assemblies,
		CancellationToken cancellationToken)
	{
		using var concurrencyGate = new SemaphoreSlim(MaxAssemblyDownloadConcurrency);
		var tasks = assemblies.Select(async assemblyPath =>
		{
			await concurrencyGate.WaitAsync(cancellationToken);
			try
			{
				var assemblyUri = VersionCheckNetworkPolicy.ResolveTrustedUri(siteUri, basePath, assemblyPath, "assembly");
				return await GetAssemblyDetailsAsync(assemblyUri, cancellationToken);
			}
			finally
			{
				concurrencyGate.Release();
			}
		});

		var allAssemblies = await Task.WhenAll(tasks);
		return allAssemblies
			.Where(static assembly => assembly?.Name is not null)
			.Select(static assembly => assembly!)
			.OrderBy(static assembly => assembly.Name, StringComparer.Ordinal)
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
		using var response = await SendAsync(dotnetConfig, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
		if (!response.IsSuccessStatusCode)
		{
			return null;
		}

		await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
		var firstMeaningfulByte = await ReadFirstMeaningfulByteAsync(stream, cancellationToken);
		return firstMeaningfulByte == (byte)'{' ? dotnetConfig : null;
	}

	private async Task<DotnetConfig?> GetDotnetConfigAsync(Uri? dotnetConfigPath, Uri siteUri, CancellationToken cancellationToken)
	{
		if (dotnetConfigPath is null)
		{
			return null;
		}

		using var response = await SendAsync(dotnetConfigPath, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
		response.EnsureSuccessStatusCode();

		var jsonBytes = await ReadContentAsBytesAsync(response.Content, dotnetConfigPath, cancellationToken);
		using var json = JsonDocument.Parse(StripUtf8Bom(jsonBytes));

		var config = ParseDotnetConfig(json.RootElement);
		var assembliesPath = new Uri(
			siteUri,
			dotnetConfigPath.OriginalString.Contains("_framework", StringComparison.Ordinal) ? "_framework/" : string.Empty);
		return config with { AssembliesPath = assembliesPath, SourceUrl = dotnetConfigPath.ToString() };
	}

	internal static IEnumerable<string> ExtractAssembliesFromResources(JsonElement resources, string propertyName)
	{
		if (!resources.TryGetProperty(propertyName, out var element)
			|| element.ValueKind == JsonValueKind.Undefined)
		{
			yield break;
		}

		if (element.ValueKind == JsonValueKind.Array)
		{
			foreach (var entry in element.EnumerateArray())
			{
				if (entry.TryGetProperty("name", out var nameProp)
					&& nameProp.GetString() is { Length: > 0 } name)
				{
					yield return name;
				}
			}

			yield break;
		}

		if (element.ValueKind == JsonValueKind.Object)
		{
			foreach (var entry in element.EnumerateObject())
			{
				if (entry.Name is { Length: > 0 })
				{
					yield return entry.Name;
				}
			}
		}
	}

	internal static DotnetConfig? ExtractBootConfigFromScript(string scriptContent)
	{
		var match = BootConfigRegex.Match(scriptContent);
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
		string? managedPath = null;
		string? packagePath = null;
		string? mainAssembly = null;
		ImmutableArray<string> assemblies = [];
		string? dotnetJsFilename = null;

		using var reader = new StringReader(configContent);
		while (reader.ReadLine() is { } line)
		{
			var parts = line.Split('=', 2);
			if (parts.Length != 2)
			{
				continue;
			}

			var field = parts[0].Trim().ToLowerInvariant();
			var value = parts[1].Trim().TrimEnd(';');

			try
			{
				switch (field)
				{
					case "config.uno_remote_managedpath":
						managedPath = JsonSerializer.Deserialize<string>(value);
						break;
					case "config.uno_app_base":
						packagePath = JsonSerializer.Deserialize<string>(value);
						break;
					case "config.assemblies_with_size":
						assemblies = JsonSerializer.Deserialize<Dictionary<string, long>>(value)?.Keys.ToImmutableArray() ?? [];
						break;
					case "config.uno_main":
						mainAssembly = JsonSerializer.Deserialize<string>(value)?.Split(']', 2)[0].TrimStart('[');
						break;
					case "config.dotnet_js_filename":
						dotnetJsFilename = JsonSerializer.Deserialize<string>(value);
						break;
				}
			}
			catch (JsonException)
			{
				continue;
			}

			if (managedPath is not null
				&& packagePath is not null
				&& mainAssembly is not null
				&& !assemblies.IsDefaultOrEmpty
				&& dotnetJsFilename is not null)
			{
				break;
			}
		}

		return new UnoConfigFields(managedPath, packagePath, mainAssembly, assemblies, dotnetJsFilename);
	}

	internal static ImmutableArray<Uri> BuildDotnetScriptCandidates(Uri siteUri, Uri? managedPath, string dotnetJsFilename)
	{
		var candidates = ImmutableArray.CreateBuilder<Uri>(2);

		if (managedPath is not null)
		{
			candidates.Add(VersionCheckNetworkPolicy.ResolveTrustedUri(siteUri, managedPath, dotnetJsFilename, "dotnet_js_filename"));
		}

		candidates.Add(VersionCheckNetworkPolicy.ResolveTrustedUri(siteUri, siteUri, $"_framework/{dotnetJsFilename}", "dotnet_js_filename"));

		return candidates
			.Distinct()
			.ToImmutableArray();
	}

	private async Task<DotnetConfig?> GetDotnetConfigFromDotnetJsAsync(
		Uri siteUri,
		string dotnetJsFilename,
		Uri? managedPath,
		CancellationToken cancellationToken)
	{
		foreach (var dotnetJsUrl in BuildDotnetScriptCandidates(siteUri, managedPath, dotnetJsFilename))
		{
			try
			{
				using var response = await SendAsync(dotnetJsUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
				if (!response.IsSuccessStatusCode)
				{
					continue;
				}

				var content = await ReadContentAsStringAsync(response.Content, dotnetJsUrl, cancellationToken);
				var config = ExtractBootConfigFromScript(content);
				if (config is not null)
				{
					return config with
					{
						AssembliesPath = EnsureTrailingSlash(new Uri(dotnetJsUrl, ".")),
						SourceUrl = dotnetJsUrl.ToString()
					};
				}
			}
			catch (HttpRequestException)
			{
				// Try the next valid bootstrapper location.
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
		}

		return null;
	}

	private async Task<HttpResponseMessage> SendAsync(
		Uri uri,
		HttpCompletionOption completionOption,
		CancellationToken cancellationToken)
	{
		for (var attempt = 0; ; attempt++)
		{
			using var request = new HttpRequestMessage(HttpMethod.Get, uri);
			var response = await httpClient.SendAsync(request, completionOption, cancellationToken);
			if (response.StatusCode != HttpStatusCode.TooManyRequests || attempt >= MaxTooManyRequestsRetries)
			{
				return response;
			}

			var retryDelay = GetRetryDelay(response, attempt);
			response.Dispose();
			await Task.Delay(retryDelay, cancellationToken);
		}
	}

	private static TimeSpan GetRetryDelay(HttpResponseMessage response, int attempt)
	{
		var retryAfter = response.Headers.RetryAfter;
		if (retryAfter?.Delta is { } delta && delta > TimeSpan.Zero)
		{
			return delta;
		}

		if (retryAfter?.Date is { } retryAt)
		{
			var delay = retryAt - DateTimeOffset.UtcNow;
			if (delay > TimeSpan.Zero)
			{
				return delay;
			}
		}

		return TooManyRequestsBackoff[Math.Min(attempt, TooManyRequestsBackoff.Length - 1)];
	}

	private static Uri EnsureTrailingSlash(Uri uri) =>
		uri.AbsoluteUri.EndsWith("/", StringComparison.Ordinal)
			? uri
			: new Uri($"{uri.AbsoluteUri}/");

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

		var assemblies = ImmutableArray<string>.Empty;
		if (root.TryGetProperty("resources", out var resources))
		{
			assemblies = ExtractAssembliesFromResources(resources, "coreAssembly")
				.Concat(ExtractAssembliesFromResources(resources, "assembly"))
				.ToImmutableArray();
		}

		return new DotnetConfig(mainAssemblyName, globalizationMode, assemblies, null, debugLevel, linkerEnabled, null);
	}

	private async Task<AssemblyVersionInfo?> GetAssemblyDetailsAsync(Uri uri, CancellationToken cancellationToken)
	{
		using var response = await SendAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
		if (!response.IsSuccessStatusCode)
		{
			return null;
		}

		try
		{
			var assemblyBytes = await ReadContentAsBytesAsync(response.Content, uri, cancellationToken);
			return await ParseAssemblyMetadataAsync(assemblyBytes);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch (Exception ex)
		{
			Trace.TraceWarning($"Unable to parse assembly '{uri}': {ex.Message}");
			return null;
		}
	}

	private static Task<AssemblyVersionInfo?> ParseAssemblyMetadataAsync(byte[] assemblyBytes)
	{
		if (assemblyBytes.Length < 4)
		{
			return Task.FromResult<AssemblyVersionInfo?>(null);
		}

		if (IsPortableExecutable(assemblyBytes))
		{
			using var peStream = new MemoryStream(assemblyBytes, writable: false);
			return Task.FromResult(ParseAssemblyMetadata(peStream));
		}

		if (IsWebAssembly(assemblyBytes))
		{
			using var webcilStream = new MemoryStream(assemblyBytes, writable: false);
			var peBytes = WebcilConverterUtil.ConvertFromWebcil(webcilStream);
			using var peStream = new MemoryStream(peBytes, writable: false);
			return Task.FromResult(ParseAssemblyMetadata(peStream));
		}

		return Task.FromResult<AssemblyVersionInfo?>(null);
	}

	private static bool IsPortableExecutable(byte[] assemblyBytes) =>
		assemblyBytes[0] == (byte)'M' && assemblyBytes[1] == (byte)'Z';

	private static bool IsWebAssembly(byte[] assemblyBytes) =>
		assemblyBytes[0] == 0x00
		&& assemblyBytes[1] == 0x61
		&& assemblyBytes[2] == 0x73
		&& assemblyBytes[3] == 0x6D;

	private static AssemblyVersionInfo? ParseAssemblyMetadata(Stream stream)
	{
		var assembly = AssemblyDefinition.ReadAssembly(stream);
		var attributes = assembly.CustomAttributes;
		var hasAttributes = false;

		var name = assembly.Name.Name;
		var version = assembly.Name.Version.ToString();
		var fileVersion = string.Empty;
		var targetFramework = string.Empty;
		string? commit = null;
		var configuration = string.Empty;

		foreach (var attribute in attributes)
		{
			hasAttributes = true;
			switch (attribute.AttributeType.Name)
			{
				case nameof(AssemblyInformationalVersionAttribute):
					var versionStr = attribute.ConstructorArguments[0].Value?.ToString() ?? string.Empty;
					version = versionStr.Split('+').FirstOrDefault() ?? string.Empty;

					if (CommitRegex.Match(versionStr) is { Success: true } match)
					{
						commit = match.Groups[1].Value;
					}
					break;
				case nameof(AssemblyFileVersionAttribute):
					fileVersion = attribute.ConstructorArguments[0].Value?.ToString() ?? string.Empty;
					break;
				case nameof(TargetFrameworkAttribute):
					targetFramework = attribute.ConstructorArguments[0].Value?.ToString() ?? string.Empty;
					break;
				case nameof(AssemblyConfigurationAttribute):
					configuration = attribute.ConstructorArguments[0].Value?.ToString() ?? string.Empty;
					break;
			}
		}

		if (!hasAttributes)
		{
			targetFramework = "WASM AOT";
		}

		return new AssemblyVersionInfo(name, version, fileVersion, configuration, targetFramework, commit);
	}

	private async Task<string> GetStringAsync(Uri uri, CancellationToken cancellationToken)
	{
		using var response = await SendAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
		response.EnsureSuccessStatusCode();
		return await ReadContentAsStringAsync(response.Content, uri, cancellationToken);
	}

	private static async Task<string> ReadContentAsStringAsync(HttpContent content, Uri sourceUri, CancellationToken cancellationToken)
	{
		var bytes = await ReadContentAsBytesAsync(content, sourceUri, cancellationToken);
		return Encoding.UTF8.GetString(bytes);
	}

	private static async Task<byte[]> ReadContentAsBytesAsync(HttpContent content, Uri sourceUri, CancellationToken cancellationToken)
	{
		await using var stream = await content.ReadAsStreamAsync(cancellationToken);
		return await ReadStreamWithLimitAsync(stream, sourceUri, cancellationToken);
	}

	private static ReadOnlyMemory<byte> StripUtf8Bom(byte[] bytes) =>
		bytes.AsSpan().StartsWith(Utf8Bom)
			? bytes.AsMemory(Utf8Bom.Length)
			: bytes;

	private static async Task<byte[]> ReadStreamWithLimitAsync(Stream stream, Uri sourceUri, CancellationToken cancellationToken)
	{
		var buffer = ArrayPool<byte>.Shared.Rent(16 * 1024);
		try
		{
			var writer = new ArrayBufferWriter<byte>();
			var totalBytes = 0;

			while (true)
			{
				var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
				if (read == 0)
				{
					return writer.WrittenMemory.ToArray();
				}

				totalBytes += read;
				if (totalBytes > VersionCheckHttp.MaxResponseBytes)
				{
					throw new InvalidOperationException($"Response from '{sourceUri}' exceeded the {VersionCheckHttp.MaxResponseBytes} byte limit.");
				}

				writer.Write(buffer.AsSpan(0, read));
			}
		}
		finally
		{
			ArrayPool<byte>.Shared.Return(buffer);
		}
	}

	private static async Task<byte?> ReadFirstMeaningfulByteAsync(Stream stream, CancellationToken cancellationToken)
	{
		var buffer = new byte[1];
		var bomIndex = 0;
		while (await stream.ReadAsync(buffer.AsMemory(0, 1), cancellationToken) == 1)
		{
			var current = buffer[0];
			if (bomIndex < Utf8Bom.Length && current == Utf8Bom[bomIndex])
			{
				bomIndex++;
				continue;
			}

			if (!char.IsWhiteSpace((char)current))
			{
				return current;
			}
		}

		return null;
	}
}
