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

using Newtonsoft.Json.Linq;

namespace Uno.Wasm.Bootstrap
{
	internal static class PwaManifestHelper
	{
		/// <summary>
		/// Sets <c>start_url</c> and <c>scope</c> to the web app base path when the
		/// manifest does not define them, so that an application hosted under a
		/// sub-folder launches and scopes there without repeating the path.
		/// </summary>
		internal static void ApplyDefaults(JObject manifest, string webAppBasePath)
		{
			// Both members are URL prefixes, so the base path must end with a separator
			if (!webAppBasePath.EndsWith("/"))
			{
				webAppBasePath += "/";
			}

			if (manifest["start_url"] is null)
			{
				manifest["start_url"] = webAppBasePath;
			}

			if (manifest["scope"] is null)
			{
				manifest["scope"] = webAppBasePath;
			}
		}
	}
}
