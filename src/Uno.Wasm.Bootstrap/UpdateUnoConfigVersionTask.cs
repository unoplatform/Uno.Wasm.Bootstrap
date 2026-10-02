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
using System.Text;
using Microsoft.Build.Framework;

namespace Uno.Wasm.Bootstrap;

/// <summary>
/// Re-stamps the <c>uno-config.js</c> version (see <see cref="UnoConfigVersion"/>) into the files that reference it,
/// after <c>uno-config.js</c> itself was rewritten.
/// </summary>
public class UpdateUnoConfigVersionTask_v0 : Microsoft.Build.Utilities.Task
{
	[Required]
	public string ConfigFile { get; set; } = "";

	/// <summary><c>index.html</c> and <c>service-worker.js</c>; missing files are skipped.</summary>
	public ITaskItem[] ReferencingFiles { get; set; } = [];

	[Output]
	public string Version { get; set; } = "";

	public override bool Execute()
	{
		if (!File.Exists(ConfigFile))
		{
			return true;
		}

		Version = UnoConfigVersion.Compute(File.ReadAllBytes(ConfigFile));

		foreach (var item in ReferencingFiles)
		{
			var path = item.ItemSpec;
			if (!File.Exists(path))
			{
				continue;
			}

			var original = File.ReadAllText(path);
			var updated = UnoConfigVersion.Apply(original, Version);
			if (updated == original)
			{
				continue;
			}

			File.WriteAllText(path, updated, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

			// Pre-compressed siblings were produced from the old content and would win content negotiation.
			File.Delete(path + ".gz");
			File.Delete(path + ".br");

			Log.LogMessage(MessageImportance.Normal, $"[Uno] Set uno-config.js version {Version} in {path}");
		}

		return true;
	}
}
