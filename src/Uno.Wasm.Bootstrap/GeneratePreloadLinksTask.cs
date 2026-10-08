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
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Build.Framework;

namespace Uno.Wasm.Bootstrap;

/// <summary>
/// Adds the startup chain's preload hints (see <see cref="PreloadLinks"/>) to a published index.html.
/// </summary>
public class GeneratePreloadLinksTask_v0 : Microsoft.Build.Utilities.Task
{
	// URLs are derived from the bootstrapper's own script tag, so they follow the base path
	private static readonly Regex _bootstrapScript = new(
		@"src=""(?<dir>[^""]*?)uno-bootstrap\.js""",
		RegexOptions.CultureInvariant);

	[Required]
	public string IndexHtml { get; set; } = "";

	[Required]
	public string FrameworkDirectory { get; set; } = "";

	[Required]
	public string ConfigFile { get; set; } = "";

	/// <summary>Fingerprint of dotnet.js, empty when it isn't fingerprinted.</summary>
	public string DotnetJsFingerprint { get; set; } = "";

	public override bool Execute()
	{
		var dotnetJsFileName = DotnetJsFingerprint is { Length: > 0 } ? $"dotnet.{DotnetJsFingerprint}.js" : "dotnet.js";
		if (Path.GetFileName(dotnetJsFileName) != dotnetJsFileName)
		{
			Log.LogMessage(MessageImportance.Normal, $"[Uno] Invalid dotnet.js fingerprint '{DotnetJsFingerprint}', skipping preload links");
			return true;
		}

		var dotnetJsPath = Path.Combine(FrameworkDirectory, dotnetJsFileName);

		if (!File.Exists(IndexHtml) || !File.Exists(dotnetJsPath) || !File.Exists(ConfigFile))
		{
			return true;
		}

		var html = File.ReadAllText(IndexHtml);
		var script = _bootstrapScript.Match(html);
		if (!script.Success)
		{
			Log.LogMessage(MessageImportance.Normal, $"[Uno] No uno-bootstrap.js script tag in {IndexHtml}, skipping preload links");
			return true;
		}

		var packageUrl = script.Groups["dir"].Value;
		var configNextToIndex = string.Equals(
			Path.GetDirectoryName(Path.GetFullPath(ConfigFile)),
			Path.GetDirectoryName(Path.GetFullPath(IndexHtml)),
			StringComparison.OrdinalIgnoreCase);

		var block = PreloadLinks.Generate(
			File.ReadAllText(dotnetJsPath),
			dotnetJsFileName,
			File.ReadAllText(ConfigFile),
			PreloadLinks.GetAppUrl(packageUrl) + "_framework/",
			PreloadLinks.GetConfigUrl(packageUrl, configNextToIndex));

		File.WriteAllText(IndexHtml, PreloadLinks.Apply(html, block), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

		// Pre-compressed siblings were produced from the old content and would win content negotiation.
		File.Delete(IndexHtml + ".gz");
		File.Delete(IndexHtml + ".br");

		return true;
	}
}
