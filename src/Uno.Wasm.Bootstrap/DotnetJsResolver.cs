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
using Newtonsoft.Json;
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
		ManifestAssetMissing,
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

	public readonly struct ManifestFile
	{
		public ManifestFile(string json, DateTime lastWriteTimeUtc)
		{
			Json = json;
			LastWriteTimeUtc = lastWriteTimeUtc;
		}

		public string Json { get; }
		public DateTime LastWriteTimeUtc { get; }
	}

	/// <summary>
	/// Reads the <c>_framework/dotnet.js</c> route of an endpoints manifest and returns the fingerprint of the
	/// file it serves, or null when the manifest has no such route, it isn't fingerprinted, or it can't be read.
	/// </summary>
	public static string? GetFingerprintFromEndpointsManifest(string endpointsJson)
		=> TryReadEndpointsManifest(endpointsJson, out var fingerprint) ? fingerprint : null;

	/// <summary>
	/// Like <see cref="GetFingerprintFromEndpointsManifest"/>, but returns false when the manifest isn't a JSON object,
	/// for example when it was left truncated by an interrupted publish.
	/// </summary>
	public static bool TryReadEndpointsManifest(string endpointsJson, out string? fingerprint)
	{
		fingerprint = null;

		JObject manifest;
		try
		{
			manifest = JObject.Parse(endpointsJson);
		}
		catch (JsonReaderException)
		{
			return false;
		}

		if (manifest["Endpoints"] is not JArray endpoints)
		{
			return true;
		}

		foreach (var endpoint in endpoints.OfType<JObject>())
		{
			var route = endpoint.Value<string>("Route") ?? "";
			var assetFile = endpoint.Value<string>("AssetFile") ?? "";

			if ((route == "_framework/dotnet.js" || route.EndsWith("/_framework/dotnet.js", StringComparison.Ordinal))
				&& assetFile.EndsWith(".js", StringComparison.Ordinal))
			{
				fingerprint = GetFingerprint(assetFile.Substring(assetFile.LastIndexOf('/') + 1));
				return true;
			}
		}

		return true;
	}

	/// <summary>
	/// Picks the manifests to resolve from. The project's own manifest is authoritative when it maps
	/// <c>dotnet.js</c>, so leftovers from other projects published to the same folder can't win. Otherwise
	/// (a hosted server, or no own manifest) the other manifests are used, newest first.
	/// </summary>
	public static IReadOnlyList<string> SelectManifests(string? projectManifestJson, IEnumerable<ManifestFile> otherManifests)
	{
		if (projectManifestJson is not null && GetFingerprintFromEndpointsManifest(projectManifestJson) is not null)
		{
			return [projectManifestJson];
		}

		return otherManifests
			.OrderByDescending(m => m.LastWriteTimeUtc)
			.Select(m => m.Json)
			.ToList();
	}

	/// <summary>
	/// Picks the current fingerprint from the endpoints manifests (one per published web project). A manifest
	/// mapping that matches a candidate wins; a mapping whose file is absent means the publish is incomplete
	/// (<see cref="Source.ManifestAssetMissing"/>) and never falls back to a possibly stale candidate.
	/// <paramref name="staleCandidates"/> lists the other fingerprinted
	/// <c>dotnet.*.js</c> files found next to it, which are left over from earlier publishes.
	/// </summary>
	public static string? Resolve(IEnumerable<string> endpointsJsons, IEnumerable<Candidate> files, out Source source, out IReadOnlyList<string> staleCandidates)
	{
		var candidates = files
			.Where(f => GetFingerprint(f.FileName) is not null)
			.OrderByDescending(f => f.LastWriteTimeUtc)
			.ToList();

		var mapped = endpointsJsons
			.Select(GetFingerprintFromEndpointsManifest)
			.OfType<string>()
			.ToList();

		string? fingerprint;
		var matched = mapped.FirstOrDefault(m => candidates.Any(c => GetFingerprint(c.FileName) == m));
		if (matched is not null)
		{
			fingerprint = matched;
			source = Source.EndpointsManifest;
		}
		else if (mapped.Count > 0)
		{
			fingerprint = null;
			source = Source.ManifestAssetMissing;
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
