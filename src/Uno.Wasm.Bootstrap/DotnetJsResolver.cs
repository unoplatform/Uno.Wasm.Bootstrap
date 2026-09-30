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
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace Uno.Wasm.Bootstrap;

/// <summary>
/// Determines which fingerprinted <c>dotnet.&lt;fingerprint&gt;.js</c> belongs to the current publish.
/// </summary>
/// <remarks>
/// A publish folder can hold several <c>dotnet.*.js</c> files when it wasn't cleaned between publishes.
/// The SDK's <c>*.staticwebassets.endpoints.json</c> is rewritten by every publish and maps the
/// <c>_framework/dotnet.js</c> route to the current file, so it is the source of truth. Without it,
/// only an unambiguous single candidate is trusted.
/// </remarks>
public static class DotnetJsResolver
{
	private static readonly Regex _fingerprintedDotnetJs = new(@"^dotnet\.([a-z0-9]+)\.js$", RegexOptions.CultureInvariant);

	public readonly struct Candidate
	{
		public Candidate(string fileName, DateTime lastWriteTimeUtc)
		{
			FileName = fileName;
			LastWriteTimeUtc = lastWriteTimeUtc;
		}

		public string FileName { get; }
		public DateTime LastWriteTimeUtc { get; }
	}

	public enum Source
	{
		None,
		EndpointsManifest,
		SingleCandidate,
		NewestCandidate,
	}

	/// <summary>Returns the fingerprint of a <c>dotnet.&lt;fingerprint&gt;.js</c> file name, or null.</summary>
	public static string? GetFingerprint(string fileName)
	{
		var match = _fingerprintedDotnetJs.Match(fileName);

		// dotnet.native.js / dotnet.runtime.js are the unfingerprinted siblings, not dotnet.js itself
		return match.Success && match.Groups[1].Value is not ("native" or "runtime")
			? match.Groups[1].Value
			: null;
	}

	/// <summary>
	/// Reads the <c>_framework/dotnet.js</c> route of an endpoints manifest and returns the fingerprint of the
	/// file it serves, or null when the manifest has no such route or it isn't fingerprinted.
	/// </summary>
	public static string? GetFingerprintFromEndpointsManifest(string endpointsJson)
	{
		var endpoints = JObject.Parse(endpointsJson)["Endpoints"] as JArray;
		if (endpoints is null)
		{
			return null;
		}

		foreach (var endpoint in endpoints.OfType<JObject>())
		{
			var route = endpoint.Value<string>("Route") ?? "";
			var assetFile = endpoint.Value<string>("AssetFile") ?? "";

			if ((route == "_framework/dotnet.js" || route.EndsWith("/_framework/dotnet.js", StringComparison.Ordinal))
				&& assetFile.EndsWith(".js", StringComparison.Ordinal))
			{
				return GetFingerprint(assetFile.Substring(assetFile.LastIndexOf('/') + 1));
			}
		}

		return null;
	}

	/// <summary>
	/// Picks the current fingerprint. <paramref name="staleCandidates"/> lists the other fingerprinted
	/// <c>dotnet.*.js</c> files found next to it, which are left over from earlier publishes.
	/// </summary>
	public static string? Resolve(string? endpointsJson, IEnumerable<Candidate> files, out Source source, out IReadOnlyList<string> staleCandidates)
	{
		var candidates = files
			.Where(f => GetFingerprint(f.FileName) is not null)
			.OrderByDescending(f => f.LastWriteTimeUtc)
			.ToList();

		var fromManifest = endpointsJson is null ? null : GetFingerprintFromEndpointsManifest(endpointsJson);

		string? fingerprint;
		if (fromManifest is not null && candidates.Any(c => GetFingerprint(c.FileName) == fromManifest))
		{
			fingerprint = fromManifest;
			source = Source.EndpointsManifest;
		}
		else if (candidates.Count == 1)
		{
			fingerprint = GetFingerprint(candidates[0].FileName);
			source = Source.SingleCandidate;
		}
		else if (candidates.Count > 1)
		{
			// Best effort only: an unchanged file skipped by an incremental publish keeps its old timestamp.
			fingerprint = GetFingerprint(candidates[0].FileName);
			source = Source.NewestCandidate;
		}
		else
		{
			fingerprint = null;
			source = Source.None;
		}

		staleCandidates = candidates
			.Select(c => c.FileName)
			.Where(f => GetFingerprint(f) != fingerprint)
			.ToList();

		return fingerprint;
	}
}
