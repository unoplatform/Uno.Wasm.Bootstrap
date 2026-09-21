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

namespace Uno.Wasm.Bootstrap
{
	internal static class WebAppBasePathHelper
	{
		internal const string DefaultBasePath = "./";

		/// <summary>
		/// Normalizes the value of the WasmShellWebAppBasePath property so that every
		/// generated reference can concatenate it directly.
		/// </summary>
		/// <remarks>
		/// An empty value is the document-relative default. A value starting with a dot
		/// stays relative, and a value containing a scheme stays as is. Any other value is
		/// a site path and gets a leading slash. Every value gets a trailing slash.
		/// </remarks>
		internal static string Normalize(string? value)
		{
			var path = value?.Trim() ?? "";

			if (path.Length == 0)
			{
				return DefaultBasePath;
			}

			path = path.Replace('\\', '/');

			var isRelative = path.StartsWith(".");
			var isAbsoluteUrl = path.Contains("://");

			if (!isRelative && !isAbsoluteUrl && !path.StartsWith("/"))
			{
				path = "/" + path;
			}

			if (!path.EndsWith("/"))
			{
				path += "/";
			}

			return path;
		}
	}
}
