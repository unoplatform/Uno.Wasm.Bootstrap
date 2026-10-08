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

using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Uno.Wasm.Bootstrap;

/// <summary>
/// Build-time handling of the loader markup in index.html.
/// </summary>
/// <remarks>
/// The bootstrapper used to apply the AppManifest colors and logo once its script had loaded the manifest, so the
/// first frames showed the default loader. Baking them into index.html makes the first paint final.
/// </remarks>
public static class LoaderMarkup
{
	public const string DefaultLogo = "https://uno-assets.platform.uno/logos/uno-splashscreen-light.png";

	private static readonly Regex _loaderTag = new(
		@"<div\b(?<attrs>[^>]*\bclass\s*=\s*""[^""]*(?<![\w-])uno-loader(?![\w-])[^""]*""[^>]*)>",
		RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

	private static readonly Regex _customAttribute = new(@"\bdata-uno-loader\s*=\s*""custom""", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

	private static readonly Regex _logoTag = new(
		@"<img\b[^>]*\bclass\s*=\s*""[^""]*(?<![\w-])logo(?![\w-])[^""]*""[^>]*>",
		RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

	private static readonly Regex _srcAttribute = new(@"\ssrc\s*=\s*""[^""]*""", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

	private static readonly Regex _barClass = new(@"\bclass\s*=\s*""[^""]*(?<![\w-])bar(?![\w-])", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

	private static readonly Regex _color = new(
		@"^(#[0-9a-f]{3,8}|[a-z]+|(rgb|rgba|hsl|hsla)\([0-9.,%\s/+-]*\))$",
		RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

	/// <summary>Whether the app replaces the loader (<c>data-uno-loader="custom"</c>), which the bootstrapper then leaves alone.</summary>
	public static bool IsCustomLoader(string html)
		=> _loaderTag.Match(html) is { Success: true } loader && _customAttribute.IsMatch(loader.Groups["attrs"].Value);

	/// <summary>Whether index.html still has the loader markup from before the progress bar and text block.</summary>
	public static bool UsesLegacyMarkup(string html)
		=> _loaderTag.Match(html) is { Success: true } loader
			&& !_customAttribute.IsMatch(loader.Groups["attrs"].Value)
			&& !_barClass.IsMatch(html.Substring(loader.Index));

	/// <summary>
	/// Reads the string properties of an AppManifest.js (<c>var UnoAppManifest = { key: "value", ... }</c>).
	/// Lenient: unknown shapes yield fewer entries rather than an error.
	/// </summary>
	public static IReadOnlyDictionary<string, string> ParseAppManifest(string script)
	{
		Dictionary<string, string> result = new(StringComparer.Ordinal);
		List<(bool IsString, string Text)> tokens = Tokenize(script);

		for (var i = 0; i + 2 < tokens.Count; i++)
		{
			if (tokens[i + 1] is { IsString: false, Text: ":" } && tokens[i + 2].IsString && IsKey(tokens[i]))
			{
				result[tokens[i].Text] = tokens[i + 2].Text;
			}
		}

		return result;
	}

	/// <summary>
	/// Applies the manifest's colors and logo to the built-in loader, the way the bootstrapper does at runtime, and marks
	/// it <c>data-manifest="baked"</c> so the bootstrapper doesn't apply them again. Custom loaders are left untouched.
	/// </summary>
	/// <param name="packageUrl">URL of the package folder, ending with a slash: local manifest images are relative to it.</param>
	public static string BakeAppManifest(string html, IReadOnlyDictionary<string, string> manifest, string packageUrl = "./")
	{
		var loader = _loaderTag.Match(html);
		if (!loader.Success || _customAttribute.IsMatch(loader.Groups["attrs"].Value))
		{
			return html;
		}

		List<string> styles = new();
		void AddColor(string property, string key)
		{
			if (manifest.TryGetValue(key, out var value) && _color.IsMatch(value.Trim()))
			{
				styles.Add($"{property}: {value.Trim()}");
			}
		}

		AddColor("--light-theme-bg-color", "lightThemeBackgroundColor");
		AddColor("--dark-theme-bg-color", "darkThemeBackgroundColor");

		// A single-theme color would override the dark theme, so per-theme colors win
		var hasPerThemeBackground = manifest.ContainsKey("lightThemeBackgroundColor") || manifest.ContainsKey("darkThemeBackgroundColor");
		if (!hasPerThemeBackground && manifest.TryGetValue("splashScreenColor", out var splashColor) && !splashColor.Trim().Equals("transparent", StringComparison.OrdinalIgnoreCase))
		{
			AddColor("background-color", "splashScreenColor");
		}

		AddColor("--accent-color", manifest.ContainsKey("lightThemeAccentColor") ? "lightThemeAccentColor" : "accentColor");
		AddColor("--dark-theme-accent-color", "darkThemeAccentColor");

		var attributes = new StringBuilder(" data-manifest=\"baked\"");
		if (styles.Count > 0)
		{
			attributes.Append($" style=\"{WebUtility.HtmlEncode(string.Join("; ", styles))}\"");
		}

		var tagEnd = loader.Index + loader.Length - 1;
		var result = html.Substring(0, tagEnd) + attributes + html.Substring(tagEnd);

		return BakeLogo(result, loader.Index, manifest, packageUrl);
	}

	private static string BakeLogo(string html, int loaderIndex, IReadOnlyDictionary<string, string> manifest, string packageUrl)
	{
		var logo = _logoTag.Match(html, loaderIndex);
		if (!logo.Success)
		{
			return html;
		}

		var light = ImageUrl(manifest, "splashScreenImage", packageUrl);
		var dark = ImageUrl(manifest, "splashScreenImageDark", packageUrl) ?? light;

		var currentSrc = _srcAttribute.Match(logo.Value);
		if (light is null)
		{
			// Like the runtime: the default logo only when index.html doesn't set its own
			if (currentSrc.Success && currentSrc.Value.Trim() != "src=\"\"")
			{
				return html;
			}

			light = dark = DefaultLogo;
		}

		var src = $" src=\"{WebUtility.HtmlEncode(light)}\"";
		var img = currentSrc.Success
			? logo.Value.Substring(0, currentSrc.Index) + src + logo.Value.Substring(currentSrc.Index + currentSrc.Length)
			: logo.Value.Insert(4, src);

		if (dark != light)
		{
			img = $"<picture><source media=\"(prefers-color-scheme: dark)\" srcset=\"{WebUtility.HtmlEncode(dark)}\" />{img}</picture>";
		}

		return html.Substring(0, logo.Index) + img + html.Substring(logo.Index + logo.Length);
	}

	private static string? ImageUrl(IReadOnlyDictionary<string, string> manifest, string key, string packageUrl)
	{
		if (!manifest.TryGetValue(key, out var value) || value.Trim() is not { Length: > 0 } url)
		{
			return null;
		}

		if (Regex.IsMatch(url, @"^https?://", RegexOptions.IgnoreCase))
		{
			return url;
		}

		// Anything else with a scheme (javascript:, data:...) isn't an image path
		if (Regex.IsMatch(url, @"^[a-z][a-z0-9+.-]*:", RegexOptions.IgnoreCase))
		{
			return null;
		}

		// Like the runtime (uno_app_base): local images are in the package folder
		return packageUrl + url.TrimStart('/');
	}

	private static bool IsKey((bool IsString, string Text) token)
		=> token.IsString || Regex.IsMatch(token.Text, @"^[A-Za-z_$][\w$]*$");

	private static List<(bool IsString, string Text)> Tokenize(string script)
	{
		List<(bool, string)> tokens = new();
		var i = 0;
		while (i < script.Length)
		{
			var c = script[i];
			if (char.IsWhiteSpace(c))
			{
				i++;
			}
			else if (c == '/' && i + 1 < script.Length && script[i + 1] == '/')
			{
				while (i < script.Length && script[i] != '\n')
				{
					i++;
				}
			}
			else if (c == '/' && i + 1 < script.Length && script[i + 1] == '*')
			{
				var end = script.IndexOf("*/", i + 2, StringComparison.Ordinal);
				i = end < 0 ? script.Length : end + 2;
			}
			else if (c is '"' or '\'')
			{
				var text = new StringBuilder();
				i++;
				while (i < script.Length && script[i] != c)
				{
					if (script[i] == '\\' && i + 1 < script.Length)
					{
						i++;
					}

					text.Append(script[i]);
					i++;
				}

				i++;
				tokens.Add((true, text.ToString()));
			}
			else if (char.IsLetterOrDigit(c) || c is '_' or '$')
			{
				var start = i;
				while (i < script.Length && (char.IsLetterOrDigit(script[i]) || script[i] is '_' or '$'))
				{
					i++;
				}

				tokens.Add((false, script.Substring(start, i - start)));
			}
			else
			{
				tokens.Add((false, c.ToString()));
				i++;
			}
		}

		return tokens;
	}
}
