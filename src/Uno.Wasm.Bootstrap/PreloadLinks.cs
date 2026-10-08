// ******************************************************************
// Copyright © 2015-2026 Uno Platform inc. All rights reserved.
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//      http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
//
// ******************************************************************

using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Uno.Wasm.Bootstrap;

/// <summary>
/// Builds the <c>&lt;link rel="preload"&gt;</c> hints for the files of the startup chain.
/// </summary>
/// <remarks>
/// Without hints the browser discovers these files one round-trip at a time: index.html, then uno-bootstrap.js,
/// then uno-config.js, then dotnet.js, then the runtime modules and dotnet.native.wasm, and the require.js
/// dependencies only once the runtime is ready. Each hint has to match the request it stands for (URL, mode,
/// integrity), or the browser downloads the file twice.
/// </remarks>
public static class PreloadLinks
{
	public const string StartMarker = "<!-- uno-preload-links -->";
	public const string EndMarker = "<!-- /uno-preload-links -->";

	private static readonly Regex _bootJson = new(@"/\*json-start\*/([\s\S]*?)/\*json-end\*/", RegexOptions.CultureInvariant);
	private static readonly Regex _dependencies = new(@"config\.uno_dependencies\s*=\s*(\[[^\]]*\])", RegexOptions.CultureInvariant);
	private static readonly Regex _existingBlock = new(Regex.Escape(StartMarker) + @"[\s\S]*?" + Regex.Escape(EndMarker) + @"\r?\n?", RegexOptions.CultureInvariant);

	/// <summary>Strips the <c>package_&lt;hash&gt;/</c> folder from its URL, e.g. <c>./package_1/</c> becomes <c>./</c>.</summary>
	public static string GetAppUrl(string packageUrl)
		=> Regex.Replace(packageUrl, @"package_[^/]+/$", "");

	/// <summary>URL of uno-config.js, which lives either next to index.html or in the package folder.</summary>
	public static string GetConfigUrl(string packageUrl, bool configNextToIndex)
		=> (configNextToIndex ? GetAppUrl(packageUrl) : packageUrl) + "uno-config.js";

	/// <param name="frameworkUrl">URL prefix of <c>_framework/</c> as seen from index.html, e.g. <c>./_framework/</c>.</param>
	/// <param name="configUrl">The uno-config.js URL exactly as uno-bootstrap.js imports it.</param>
	public static string Generate(string dotnetJsContent, string dotnetJsFileName, string unoConfigContent, string frameworkUrl, string configUrl)
	{
		var links = new List<string>
		{
			ModulePreload(configUrl),
			ModulePreload(frameworkUrl + dotnetJsFileName),
		};

		var match = _bootJson.Match(dotnetJsContent);
		if (match.Success && TryParse(() => JObject.Parse(match.Groups[1].Value)) is { } bootConfig && bootConfig["resources"] is JObject resources)
		{
			foreach (var name in Names(resources["jsModuleRuntime"]).Concat(Names(resources["jsModuleNative"])))
			{
				links.Add(ModulePreload(frameworkUrl + name));
			}

			// The runtime fetches dotnet.native.wasm in cors mode. No integrity attribute: Chrome ignores it on
			// fetch preloads and logs a warning (crbug.com/981419).
			foreach (var name in Names(resources["wasmNative"]))
			{
				links.Add($"<link rel=\"preload\" href=\"{Attribute(frameworkUrl + name)}\" as=\"fetch\" type=\"application/wasm\" crossorigin=\"anonymous\" />");
			}
		}

		// Loaded by require.js as classic scripts once the runtime is ready
		var dependencies = _dependencies.Match(unoConfigContent);
		if (dependencies.Success && TryParse(() => JArray.Parse(dependencies.Groups[1].Value)) is { } dependencyArray)
		{
			foreach (var dependency in dependencyArray.Values<string>().OfType<string>())
			{
				links.Add($"<link rel=\"preload\" href=\"{Attribute(dependency.EndsWith(".js") ? dependency : dependency + ".js")}\" as=\"script\" />");
			}
		}

		var builder = new StringBuilder();
		builder.AppendLine(StartMarker);
		foreach (var link in links)
		{
			builder.AppendLine(link);
		}
		builder.AppendLine(EndMarker);
		return builder.ToString();
	}

	/// <summary>Inserts <paramref name="block"/> before <c>&lt;/head&gt;</c>, replacing a block from an earlier run.</summary>
	public static string Apply(string html, string block)
	{
		html = _existingBlock.Replace(html, "");

		var headEnd = html.IndexOf("</head>", System.StringComparison.OrdinalIgnoreCase);
		return headEnd < 0 ? html : html.Insert(headEnd, block);
	}

	// A malformed section only loses its hints, it never fails the publish
	private static T? TryParse<T>(System.Func<T> parse) where T : class
	{
		try
		{
			return parse();
		}
		catch (JsonReaderException)
		{
			return null;
		}
	}

	private static IEnumerable<string> Names(JToken? entries)
		=> entries switch
		{
			JArray array => array.Select(e => e.Value<string>("name") ?? "").Where(n => n.Length > 0), // .NET 10+
			JObject dictionary => dictionary.Properties().Select(p => p.Name),
			_ => [],
		};

	private static string ModulePreload(string url) => $"<link rel=\"modulepreload\" href=\"{Attribute(url)}\" />";

	private static string Attribute(string value) => WebUtility.HtmlEncode(value);
}
