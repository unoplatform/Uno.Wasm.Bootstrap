
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
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Uno.Wasm.Bootstrap.Extensions;

namespace Uno.Wasm.Bootstrap;

public class GenerateUnoAssetsManifest_v0 : Microsoft.Build.Utilities.Task
{
	private string _intermediateAssetsPath = "";

	[Required]
	public ITaskItem[] StaticWebAsset { get; set; } = [];

	[Required]
	public string IntermediateOutputPath { get; set; } = "";

	[Output]
	public ITaskItem[] UnoAssetsFile { get; set; } = [];

	public override bool Execute()
	{
		_intermediateAssetsPath = Path.Combine(IntermediateOutputPath, "unowwwrootassets");

		List<string> assets = new();

		// Grab the list of all the staticwebassets provided to be available in uno-assets.txt
		foreach(var asset in StaticWebAsset)
		{
			var assetPath = Path.GetDirectoryName(asset.GetMetadata("RelativePath")) + "/" + Path.GetFileName(asset.GetMetadata("FullPath"));
			assets.Add(assetPath.Replace("\\", "/"));
		}

		var assetsFilePath = Path.Combine(_intermediateAssetsPath, "uno-assets.txt");
		File.WriteAllLines(assetsFilePath, assets);
		AddStaticAsset(Path.GetFileName(assetsFilePath), assetsFilePath);

		return true;
	}

	private void AddStaticAsset(string targetPath, string filePath)
	{
		var contentRoot = targetPath.StartsWith(_intermediateAssetsPath)
					? _intermediateAssetsPath
					: Path.GetDirectoryName(filePath);

		TaskItem indexMetadata = new(
			filePath, new Dictionary<string, string>
			{
				["CopyToOutputDirectory"] = "PreserveNewest",
				["ContentRoot"] = contentRoot,
				["Link"] = "wwwroot/" + targetPath,
			});

		UnoAssetsFile = UnoAssetsFile.Concat([indexMetadata]).ToArray();
	}
}
