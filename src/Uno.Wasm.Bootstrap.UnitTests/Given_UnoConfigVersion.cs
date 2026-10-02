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
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Uno.Wasm.Bootstrap;

namespace Uno.Wasm.Bootstrap.UnitTests
{
	[TestClass]
	public class Given_UnoConfigVersion
	{
		[TestMethod]
		public void When_Compute_Then_Twelve_Lowercase_Hex_Of_Sha256()
			// sha256("abc") = ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad
			=> Assert.AreEqual("ba7816bf8f01", UnoConfigVersion.Compute(Encoding.UTF8.GetBytes("abc")));

		[TestMethod]
		public void When_Config_Changes_Then_Version_Changes()
			=> Assert.AreNotEqual(
				UnoConfigVersion.Compute(Encoding.UTF8.GetBytes("config.dotnet_js_filename = \"dotnet.aaaaaaaaaa.js\";")),
				UnoConfigVersion.Compute(Encoding.UTF8.GetBytes("config.dotnet_js_filename = \"dotnet.bbbbbbbbbb.js\";")));

		[TestMethod]
		public void When_Index_Html_Script_Tag()
			=> Assert.AreEqual(
				"<script type=\"module\" src=\"/package_1234/uno-bootstrap.js?v=abc\"></script>",
				UnoConfigVersion.Apply("<script type=\"module\" src=\"/package_1234/uno-bootstrap.js\"></script>", "abc"));

		[TestMethod]
		public void When_Already_Versioned_Then_Replaced()
			=> Assert.AreEqual(
				"<script type=\"module\" src=\"./uno-bootstrap.js?v=new\"></script>",
				UnoConfigVersion.Apply("<script type=\"module\" src=\"./uno-bootstrap.js?v=old123\"></script>", "new"));

		[TestMethod]
		public void When_Service_Worker_Import()
			=> Assert.AreEqual(
				"import { config as unoConfig } from \"/package_1234/uno-config.js?v=abc\";",
				UnoConfigVersion.Apply("import { config as unoConfig } from \"/package_1234/uno-config.js\";", "abc"));

		[TestMethod]
		public void When_Single_Quotes()
			=> Assert.AreEqual("src='uno-bootstrap.js?v=abc'", UnoConfigVersion.Apply("src='uno-bootstrap.js'", "abc"));

		[TestMethod]
		public void When_Other_Scripts_Then_Untouched()
		{
			const string html = "<script src=\"/package_1234/require.js\"></script><script src=\"/my-uno-bootstrap.js.map\"></script><script src=\"/my-uno-bootstrap.js\"></script>";
			Assert.AreEqual(html, UnoConfigVersion.Apply(html, "abc"));
		}
	}
}
