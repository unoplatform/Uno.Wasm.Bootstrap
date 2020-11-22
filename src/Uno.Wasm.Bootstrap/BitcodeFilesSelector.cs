// ******************************************************************
// Copyright � 2015-2018 nventive inc. All rights reserved.
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
using System.Security.Cryptography;

namespace Uno.Wasm.Bootstrap
{
	internal class BitcodeFilesSelector
	{
		internal static string[] Filter(Version emscriptenVersion, string[] bitcodeFiles)
		{
			var files = new List<string>();

			foreach (var group in bitcodeFiles.GroupBy(f => Path.GetFileName(f)))
			{
				var versions = group
					.AsEnumerable()
					.Select(ParsePackageSpec)
					.OrderBy(v => v.version)
					.ToList();

				if(versions.Count == 1)
				{
					files.Add(versions.Select(v => v.fullPath).First());
				}
				else
				{
					var validVersions = versions
						.Where(v => v.version >= emscriptenVersion)
						.Select(v => v.fullPath)
						.ToArray();

					files.Add(validVersions.FirstOrDefault() ?? versions.Last().fullPath);
				}
			}

			return files.ToArray();
		}

		private static (string? name, Version? version, string fullPath) ParsePackageSpec(string arg)
		{
			var fileName = Path.GetFileName(arg);
			var pathToVersion = Path.GetDirectoryName(arg);
			var versionText = Path.GetFileName(pathToVersion);
			var fileSpec = Path.GetFileName(Path.GetDirectoryName(pathToVersion)) ?? "";

			if(fileSpec.Equals(fileName, StringComparison.OrdinalIgnoreCase)
				&& Version.TryParse(versionText, out var version))
			{
				return (fileName, version, arg);
			}
			else
			{
				return (null, null, arg);
			}
		}
	}
}
