// ******************************************************************
// Copyright © 2015-2022 Uno Platform inc. All rights reserved.
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

using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Uno.Wasm.Bootstrap
{
	internal static class IndexHtmlHelper
	{
		/// <summary>
		/// Rewrites the double-quoted references of <paramref name="html"/> that point at a
		/// file deployed to the package folder so that they include the web app base path
		/// and the package folder name. Other references are left untouched.
		/// </summary>
		/// <param name="html">The index.html content.</param>
		/// <param name="webAppBasePath">The normalized web app base path, such as <c>./</c> or <c>/app/</c>.</param>
		/// <param name="packageFolder">The package folder name, such as <c>package_abc</c>.</param>
		/// <param name="packageFiles">The paths of the files deployed to the package folder, relative to it.</param>
		internal static string RelocatePackageReferences(string html, string webAppBasePath, string packageFolder, ISet<string> packageFiles)
		{
			var prefixes = webAppBasePath == "./"
				? Regex.Escape("./")
				: Regex.Escape("./") + "|" + Regex.Escape(webAppBasePath);

			// "<prefix><path><query or fragment>"
			var reference = new Regex($"\"(?:{prefixes})(?<path>[^\"?#]*)(?<suffix>[^\"]*)\"");

			return reference.Replace(html, match =>
			{
				var path = match.Groups["path"].Value;

				return packageFiles.Contains(path)
					? $"\"{webAppBasePath}{packageFolder}/{path}{match.Groups["suffix"].Value}\""
					: match.Value;
			});
		}
	}
}
