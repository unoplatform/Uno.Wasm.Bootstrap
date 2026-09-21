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
using Newtonsoft.Json.Linq;

namespace Uno.Wasm.Bootstrap.UnitTests
{
	[TestClass]
	public class Given_PwaManifestHelper
	{
		[TestMethod]
		[DataRow("/")]
		[DataRow("/app/")]
		[DataRow("./")]
		public void When_ManifestHasNoStartUrlOrScope_Then_BasePathIsUsed(string basePath)
		{
			var manifest = JObject.Parse("""{ "name": "App" }""");

			PwaManifestHelper.ApplyDefaults(manifest, basePath);

			Assert.AreEqual(basePath, manifest["start_url"]?.Value<string>());
			Assert.AreEqual(basePath, manifest["scope"]?.Value<string>());
		}

		[TestMethod]
		public void When_ManifestDefinesStartUrlAndScope_Then_TheyAreKept()
		{
			var manifest = JObject.Parse("""{ "start_url": "/index.html", "scope": "/portal/" }""");

			PwaManifestHelper.ApplyDefaults(manifest, "/app/");

			Assert.AreEqual("/index.html", manifest["start_url"]?.Value<string>());
			Assert.AreEqual("/portal/", manifest["scope"]?.Value<string>());
		}

		[TestMethod]
		public void When_ManifestDefinesOnlyStartUrl_Then_OnlyScopeIsDefaulted()
		{
			var manifest = JObject.Parse("""{ "start_url": "/index.html" }""");

			PwaManifestHelper.ApplyDefaults(manifest, "/app/");

			Assert.AreEqual("/index.html", manifest["start_url"]?.Value<string>());
			Assert.AreEqual("/app/", manifest["scope"]?.Value<string>());
		}
	}
}
