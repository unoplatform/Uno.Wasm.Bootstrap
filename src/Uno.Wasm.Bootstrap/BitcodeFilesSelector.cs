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
using System.IO;
using System.Linq;

namespace Uno.Wasm.Bootstrap;

internal class BitcodeFilesSelector
{
	private record PackageSpec(string? Name, Version? Version, string Fullpath, string[] Features);

	internal static string[] Filter(Version emscriptenVersion, string[] requiredFeatures, string[] bitcodeFiles)
	{
		var files = new List<string>();

		foreach (var group in bitcodeFiles.GroupBy(f => Path.GetFileName(f)))
		{
			var versions = group
				.AsEnumerable()
				.Select(ParsePackageSpec)
				.Where(s => requiredFeatures.All(r => s.Features.Contains(r)))
				.OrderByDescending(v => v.Version)
				.ToList();

			if(versions.Count == 1)
			{
				files.Add(versions.Select(v => v.Fullpath).First());
			}
			else if(versions.Count > 1)
			{
				var validVersions = versions
					.Where(v => v.Version <= emscriptenVersion)
					.Select(v => v.Fullpath)
					.ToArray();

				files.Add(validVersions.FirstOrDefault() ?? versions.Last().Fullpath);
			}
		}

		return files.ToArray();
	}

	private static PackageSpec ParsePackageSpec(string arg)
	{
		var parts = arg.Split(Path.DirectorySeparatorChar);

		var fileName = parts[parts.Length - 1];

		if (parts.Length > 1)
		{
			if (Version.TryParse(parts[parts.Length - 2], out var version))
			{
				// Implicit single-threaded definition
				if (parts[parts.Length - 3].Equals(fileName, StringComparison.OrdinalIgnoreCase))
				{
					return new(fileName, version, arg, new[] { "st" });
				}
			}
			else if (parts.Length > 2)
			{
				// Featured explicit definition
				var features = parts[parts.Length - 2].Split(',');

				if (Version.TryParse(parts[parts.Length - 3], out version))
				{
					if (parts[parts.Length - 4].Equals(fileName, StringComparison.OrdinalIgnoreCase))
					{
						return new(fileName, version, arg, features);
					}
				}
			}
		}

		return new (null, null, arg, new[] { "st" });
	}
}
