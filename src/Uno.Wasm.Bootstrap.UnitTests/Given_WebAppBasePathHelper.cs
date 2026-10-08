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
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Uno.Wasm.Bootstrap.UnitTests
{
	[TestClass]
	public class Given_WebAppBasePathHelper
	{
		[TestMethod]
		[DataRow(null, "./")]
		[DataRow("", "./")]
		[DataRow("   ", "./")]
		[DataRow("/", "/")]
		[DataRow("./", "./")]
		[DataRow(".", "./")]
		[DataRow("../", "../")]
		[DataRow("..", "../")]
		[DataRow("/app/", "/app/")]
		[DataRow("/app", "/app/")]
		[DataRow("app/", "/app/")]
		[DataRow("app", "/app/")]
		[DataRow("app/sub", "/app/sub/")]
		[DataRow(" /app ", "/app/")]
		[DataRow("\\app", "/app/")]
		[DataRow("https://cdn.example.com/app", "https://cdn.example.com/app/")]
		[DataRow("https://cdn.example.com/app/", "https://cdn.example.com/app/")]
		public void When_Normalize_Then_PathHasLeadingAndTrailingSlash(string input, string expected)
		{
			Assert.AreEqual(expected, WebAppBasePathHelper.Normalize(input));
		}

		[TestMethod]
		[DataRow("./package_x/", "\"./package_x/AppManifest\"")]
		[DataRow("/package_x/", "\"/package_x/AppManifest.js\"")]
		[DataRow("/app/package_x/", "\"/app/package_x/AppManifest.js\"")]
		[DataRow("https://cdn.example.com/app/package_x/", "\"https://cdn.example.com/app/package_x/AppManifest.js\"")]
		public void When_BuildDependencyPath_Then_SitePathsAndUrlsKeepTheExtension(string baseLookup, string expected)
		{
			Assert.AreEqual(expected, WebAppBasePathHelper.BuildDependencyPath("WasmScripts/AppManifest.js", baseLookup));
		}
	}
}
