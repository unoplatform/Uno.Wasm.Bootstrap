
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

public class GenerateUnoNativeAssetsTask_v0 : Microsoft.Build.Utilities.Task
{
	public bool EnableThreads { get; set; }

	public ITaskItem[]? Assets { get; set; }

	public string EmscriptenVersion { get; set; } = "";

	[Required]
	public string CurrentProjectPath { get; set; } = "";

	[Output]
	public ITaskItem[]? NativeFileReference { get; set; } = [];

	public override bool Execute()
	{
		GenerateBitcodeFiles();

		return true;
	}

	private void GenerateBitcodeFiles()
	{
		var bitcodeFiles = Assets
			?.Where(a => a.ItemSpec.EndsWith(".o") || a.ItemSpec.EndsWith(".a"))
			.Where(a => !bool.TryParse(a.GetMetadata("UnoAotCompile"), out var compile) || compile)
			.Select(a => a.GetFilePaths(Log, CurrentProjectPath).fullPath)
			.ToArray()
			?? [];

		List<string> features = new()
			{
				EnableThreads ? "mt" : "st",
				"simd"
			};

		Log.LogMessage(MessageImportance.Low, $"Bitcode files features lookup filter: {string.Join(",", features)}");

		if (Version.TryParse(EmscriptenVersion, out var emsdkVersion))
		{
			var list = BitcodeFilesSelector.Filter(emsdkVersion, features.ToArray(), bitcodeFiles);

			NativeFileReference = list.Select(i => new TaskItem(i)).ToArray();
		}
		else
		{
			Log.LogMessage(MessageImportance.Low, $"EmscriptenVersion is not set, skipping native assets");
		}
	}

}
