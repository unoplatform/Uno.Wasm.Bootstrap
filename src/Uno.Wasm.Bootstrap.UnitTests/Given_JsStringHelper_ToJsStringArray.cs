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
	public class Given_JsStringHelper_ToJsStringArray
	{
		[TestMethod]
		[DataRow(null, "[]")]
		[DataRow("", "[]")]
		[DataRow(" ; ;", "[]")]
		[DataRow("*.ttf", "[\"*.ttf\"]")]
		[DataRow("*.ttf; pwa-images/** ;", "[\"*.ttf\", \"pwa-images/**\"]")]
		[DataRow("a\"b", "[\"a\\\"b\"]")]
		public void When_ToJsStringArray(string? input, string expected)
			=> Assert.AreEqual(expected, JsStringHelper.ToJsStringArray(input));
	}
}
