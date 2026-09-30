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

using System.IO;
using System.Linq;
using Microsoft.Build.Framework;

namespace Uno.Wasm.Bootstrap;

/// <summary>
/// Finds the <c>dotnet.&lt;fingerprint&gt;.js</c> produced by the current publish. See <see cref="DotnetJsResolver"/>.
/// </summary>
public class ResolvePublishedDotnetJsTask_v0 : Microsoft.Build.Utilities.Task
{
	/// <summary>The publish directory (the one containing <c>wwwroot</c>).</summary>
	[Required]
	public string PublishDirectory { get; set; } = "";

	[Output]
	public string Fingerprint { get; set; } = "";

	public override bool Execute()
	{
		var frameworkDirectory = Path.Combine(PublishDirectory, "wwwroot", "_framework");
		if (!Directory.Exists(frameworkDirectory))
		{
			return true;
		}

		var candidates = Directory
			.GetFiles(frameworkDirectory, "dotnet.*.js")
			.Select(f => new DotnetJsResolver.Candidate(Path.GetFileName(f), File.GetLastWriteTimeUtc(f)));

		// One manifest per published web project; with a hosted server there may be several, any one mapping dotnet.js wins.
		string? endpointsJson = null;
		foreach (var manifest in Directory.GetFiles(PublishDirectory, "*.staticwebassets.endpoints.json"))
		{
			var json = File.ReadAllText(manifest);
			if (DotnetJsResolver.GetFingerprintFromEndpointsManifest(json) is not null)
			{
				endpointsJson = json;
				break;
			}
		}

		Fingerprint = DotnetJsResolver.Resolve(endpointsJson, candidates, out var source, out var stale) ?? "";

		if (source == DotnetJsResolver.Source.NewestCandidate)
		{
			Log.LogWarning(
				subcategory: null,
				warningCode: "UNOWASM003",
				helpKeyword: null,
				file: null, lineNumber: 0, columnNumber: 0, endLineNumber: 0, endColumnNumber: 0,
				message: $"[Uno] Found several dotnet.*.js files in {frameworkDirectory} and no endpoints manifest to tell them apart; using the newest, dotnet.{Fingerprint}.js. Clean the publish directory before publishing. Other files: {string.Join(", ", stale)}");
		}
		else if (stale.Count > 0)
		{
			Log.LogMessage(MessageImportance.Normal, $"[Uno] Ignoring dotnet.*.js files left over from earlier publishes: {string.Join(", ", stale)}");
		}

		return true;
	}
}
