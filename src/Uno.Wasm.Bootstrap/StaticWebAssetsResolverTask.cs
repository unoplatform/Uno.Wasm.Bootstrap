
// ******************************************************************
// Copyright � 2015-2022 Uno Platform inc. All rights reserved.
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
// 
// This file is based on the work from https://github.com/praeclarum/Ooui
// 
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Uno.Wasm.Bootstrap.Extensions;

namespace Uno.Wasm.Bootstrap;

public class StaticWebAssetsResolverTask_v0 : Microsoft.Build.Utilities.Task
{
	public string? WebAppBasePath { get; set; }

	[Required]
	public string AssemblyName { get; private set; } = "";

	[Required]
	public string DistPath { get; private set; } = "";

	[Required]
	public string ProjectDirectory { get; private set; } = "";

	[Output]
	public ITaskItem[]? StaticWebAsset { get; private set; }

	public override bool Execute()
	{
		var fixedDistPath = PathHelper.FixupPath(DistPath);
		var projectDirectory = PathHelper.FixupPath(ProjectDirectory);

		if (Directory.Exists(fixedDistPath))
		{
			var wwwRootBasePath = Path.Combine(projectDirectory, "wwwroot");

			var distFiles = Directory.EnumerateFiles(fixedDistPath, "*.*", SearchOption.AllDirectories).ToArray();
			var wwwRootAssets = Directory.Exists(wwwRootBasePath)
				? Directory.EnumerateFiles(wwwRootBasePath, "*.*", SearchOption.AllDirectories)
				: Enumerable.Empty<string>();

			Log.LogMessage(MessageImportance.Low, $"Found {distFiles.Length} assets");

			var filteredAssets = distFiles
				.Where(a => !wwwRootAssets
					.Select(wa => wa.Replace(wwwRootBasePath, ""))
					.Any(wa => a.Replace(fixedDistPath, "") == wa))
				.ToArray()
				;

			Log.LogMessage(MessageImportance.Low, $"Found {filteredAssets.Length} filtered assets");

			StaticWebAsset =
				filteredAssets.Select(asset => new TaskItem(asset, GetMetadata(asset, fixedDistPath)))
				.ToArray();
		}
		else
		{
			Log.LogMessage(MessageImportance.Low, $"The folder {fixedDistPath} does not exist, skipping StaticAssets generation");
		}

		return true;
	}

	private Dictionary<string, string> GetMetadata(string asset, string fixedDistPath)
	{
		var dict = new Dictionary<string, string>
		{
			["SourceType"] = "Discovered",
			["SourceId"] = AssemblyName,
			["ContentRoot"] = fixedDistPath,
			["RelativePath"] = asset.Replace(fixedDistPath, ""),
			["BasePath"] = WebAppBasePath is { Length: > 0 } ? WebAppBasePath : "/",
			["AssetKind"] = "All",
			["AssetMode"] = "All",
			["AssetRole"] = "Primary",
			["OriginalItemSpec"] = asset,
		};

		var (fingerprint, integrity) = ComputeFingerprintAndIntegrity(asset, asset);
		dict.Add("Fingerprint", fingerprint);
		dict.Add("Integrity", integrity);
		return dict;
	}

	// https://github.com/dotnet/sdk/blob/cc17704acfbee4b2ef49a82aa6f65aaa9cafffef/src/StaticWebAssetsSdk/Tasks/Data/StaticWebAsset.cs#L233-L248
	private static (string fingerprint, string integrity) ComputeFingerprintAndIntegrity(string identity, string originalItemSpec)
	{
		using var file = File.Exists(identity) ?
			File.OpenRead(identity) :
			(File.Exists(originalItemSpec) ?
				File.OpenRead(originalItemSpec) :
				throw new InvalidOperationException($"No file exists for the asset at either location '{identity}' or '{originalItemSpec}'."));

#if NET6_0_OR_GREATER
        var hash = SHA256.HashData(file);
#else
		using var sha = SHA256.Create();
		var hash = sha.ComputeHash(file);
#endif
		return (ToBase36(hash), Convert.ToBase64String(hash));
	}

	private static string ToBase36(byte[] hash)
	{
		const string chars = "0123456789abcdefghijklmnopqrstuvwxyz";

		var result = new char[10];
		var dividend = BigInteger.Abs(new BigInteger(hash.AsSpan().Slice(0, 9).ToArray()));
		for (var i = 0; i < 10; i++)
		{
			dividend = BigInteger.DivRem(dividend, 36, out var remainder);
			result[i] = chars[(int)remainder];
		}

		return new string(result);
	}
}
