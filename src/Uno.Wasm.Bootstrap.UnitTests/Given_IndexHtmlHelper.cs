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
using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Uno.Wasm.Bootstrap.UnitTests
{
	[TestClass]
	public class Given_IndexHtmlHelper
	{
		private static readonly HashSet<string> PackageFiles = new(StringComparer.OrdinalIgnoreCase)
		{
			"require.js",
			"uno-bootstrap.js",
			"uno-bootstrap.css",
			"Assets/logo.png",
		};

		[TestMethod]
		[DataRow("./", "<script src=\"./require.js\"></script>", "<script src=\"./package_x/require.js\"></script>")]
		[DataRow("/", "<script src=\"./require.js\"></script>", "<script src=\"/package_x/require.js\"></script>")]
		[DataRow("/app/", "<script src=\"./require.js\"></script>", "<script src=\"/app/package_x/require.js\"></script>")]
		[DataRow("/", "<script src=\"/uno-bootstrap.js\"></script>", "<script src=\"/package_x/uno-bootstrap.js\"></script>")]
		[DataRow("/app/", "<link href=\"/app/uno-bootstrap.css\" />", "<link href=\"/app/package_x/uno-bootstrap.css\" />")]
		[DataRow("/", "<img src=\"./Assets/logo.png?v=2#top\" />", "<img src=\"/package_x/Assets/logo.png?v=2#top\" />")]
		public void When_ReferenceIsPackageFile_Then_ItIsRelocated(string basePath, string html, string expected)
		{
			Assert.AreEqual(expected, IndexHtmlHelper.RelocatePackageReferences(html, basePath, "package_x", PackageFiles));
		}

		[TestMethod]
		[DataRow("/", "<a href=\"/\">home</a>")]
		[DataRow("/", "<link rel=\"icon\" href=\"/favicon.ico\" />")]
		[DataRow("/", "<script>fetch(\"/api/status\")</script>")]
		[DataRow("/app/", "<a href=\"/app/about\">about</a>")]
		[DataRow("/", "<script src=\"./vendor/other.js\"></script>")]
		[DataRow("/", "<script src=\"https://cdn.example.com/lib.js\"></script>")]
		[DataRow("./", "<link rel=\"icon\" href=\"/favicon.ico\" />")]
		public void When_ReferenceIsNotPackageFile_Then_ItIsUnchanged(string basePath, string html)
		{
			Assert.AreEqual(html, IndexHtmlHelper.RelocatePackageReferences(html, basePath, "package_x", PackageFiles));
		}

		[TestMethod]
		public void When_TemplateIsProcessed_Then_OnlyPackageReferencesChange()
		{
			const string html = """
				<script type="text/javascript" src="./require.js"></script>
				<script type="module" src="./uno-bootstrap.js"></script>
				<link rel="stylesheet" href="/uno-bootstrap.css" />
				<link rel="manifest" href="/manifest.json" />
				<a href="/">home</a>
				""";

			const string expected = """
				<script type="text/javascript" src="/package_x/require.js"></script>
				<script type="module" src="/package_x/uno-bootstrap.js"></script>
				<link rel="stylesheet" href="/package_x/uno-bootstrap.css" />
				<link rel="manifest" href="/manifest.json" />
				<a href="/">home</a>
				""";

			Assert.AreEqual(expected, IndexHtmlHelper.RelocatePackageReferences(html, "/", "package_x", PackageFiles));
		}
	}
}
