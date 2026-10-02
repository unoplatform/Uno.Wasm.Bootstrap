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
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Uno.Wasm.Bootstrap;

namespace Uno.Wasm.Bootstrap.UnitTests
{
	[TestClass]
	public class Given_PreloadLinks
	{
		// Shape of the boot config the SDK embeds in dotnet.<fingerprint>.js
		private const string DotnetJs =
			"var a=1;/*json-start*/{\"mainAssemblyName\":\"App\",\"resources\":{"
			+ "\"jsModuleNative\":[{\"name\":\"dotnet.native.nnnnnnnnnn.js\"}],"
			+ "\"jsModuleRuntime\":[{\"name\":\"dotnet.runtime.rrrrrrrrrr.js\"}],"
			+ "\"wasmNative\":[{\"name\":\"dotnet.native.wwwwwwwwww.wasm\",\"hash\":\"sha256-abc+/=\",\"cache\":\"force-cache\"}]"
			+ "}}/*json-end*/var b=2;";

		private const string UnoConfig =
			"let config = {};\nconfig.uno_dependencies = [\"./package_1/helpers.js\", \"./package_1/AppManifest\"];\nexport { config };";

		private static string Generate()
			=> PreloadLinks.Generate(DotnetJs, "dotnet.dddddddddd.js", UnoConfig, "./_framework/", "./package_1/uno-config.js?v=123");

		[TestMethod]
		public void When_Generate_Then_Modules_Are_Modulepreloaded()
		{
			var block = Generate();

			StringAssert.Contains(block, "<link rel=\"modulepreload\" href=\"./package_1/uno-config.js?v=123\" />");
			StringAssert.Contains(block, "<link rel=\"modulepreload\" href=\"./_framework/dotnet.dddddddddd.js\" />");
			StringAssert.Contains(block, "<link rel=\"modulepreload\" href=\"./_framework/dotnet.runtime.rrrrrrrrrr.js\" />");
			StringAssert.Contains(block, "<link rel=\"modulepreload\" href=\"./_framework/dotnet.native.nnnnnnnnnn.js\" />");
		}

		[TestMethod]
		public void When_Generate_Then_Wasm_Matches_The_Runtime_Fetch()
			=> StringAssert.Contains(
				Generate(),
				"<link rel=\"preload\" href=\"./_framework/dotnet.native.wwwwwwwwww.wasm\" as=\"fetch\" type=\"application/wasm\" crossorigin=\"anonymous\" />");

		[TestMethod]
		public void When_Generate_Then_Require_Dependencies_Are_Scripts()
		{
			var block = Generate();

			StringAssert.Contains(block, "<link rel=\"preload\" href=\"./package_1/helpers.js\" as=\"script\" />");
			StringAssert.Contains(block, "<link rel=\"preload\" href=\"./package_1/AppManifest.js\" as=\"script\" />");
		}

		[TestMethod]
		public void When_No_Boot_Config_Then_Only_Entry_Modules()
		{
			var block = PreloadLinks.Generate("var a=1;", "dotnet.js", "let config = {};", "/_framework/", "/package_1/uno-config.js");

			StringAssert.Contains(block, "href=\"/_framework/dotnet.js\"");
			Assert.IsFalse(block.Contains(".wasm"));
		}

		[TestMethod]
		public void When_Dictionary_Shaped_Resources_Then_Wasm_Is_Preloaded()
		{
			var dotnetJs = "/*json-start*/{\"resources\":{\"wasmNative\":{\"dotnet.native.wasm\":\"sha256-abc\"}}}/*json-end*/";

			var block = PreloadLinks.Generate(dotnetJs, "dotnet.js", "let config = {};", "./_framework/", "./package_1/uno-config.js");

			StringAssert.Contains(block, "<link rel=\"preload\" href=\"./_framework/dotnet.native.wasm\" as=\"fetch\" type=\"application/wasm\" crossorigin=\"anonymous\" />");
		}

		[TestMethod]
		public void When_Apply_Then_Inserted_Before_Head_End()
		{
			var html = PreloadLinks.Apply("<html><head><title>a</title></head><body></body></html>", "<!-- uno-preload-links -->\nX\n<!-- /uno-preload-links -->\n");

			Assert.AreEqual("<html><head><title>a</title><!-- uno-preload-links -->\nX\n<!-- /uno-preload-links -->\n</head><body></body></html>", html);
		}

		[TestMethod]
		public void When_Apply_Twice_Then_Block_Is_Replaced()
		{
			var once = PreloadLinks.Apply("<head></head>", PreloadLinks.StartMarker + "\nold\n" + PreloadLinks.EndMarker + "\n");
			var twice = PreloadLinks.Apply(once, PreloadLinks.StartMarker + "\nnew\n" + PreloadLinks.EndMarker + "\n");

			Assert.AreEqual("<head>" + PreloadLinks.StartMarker + "\nnew\n" + PreloadLinks.EndMarker + "\n</head>", twice);
		}
	}
}
