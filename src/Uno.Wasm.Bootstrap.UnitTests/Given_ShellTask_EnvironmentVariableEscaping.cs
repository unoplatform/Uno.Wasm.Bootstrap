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
	public class Given_ShellTask_EnvironmentVariableEscaping
	{
		[TestMethod]
		[DataRow("plain value", "plain value")]
		[DataRow("value with \"quotes\"", "value with \\\"quotes\\\"")]
		[DataRow("value with \\backslash", "value with \\\\backslash")]
		[DataRow("{\"sub\":\"7074\"}", "{\\\"sub\\\":\\\"7074\\\"}")]
		[DataRow("back\\slash and \"quote\"", "back\\\\slash and \\\"quote\\\"")]
		[DataRow("", "")]
		public void When_EscapeJsString_Then_SpecialCharsAreCorrectlyEscaped(string input, string expected)
		{
			var result = ShellTask_v0.EscapeJsString(input);
			Assert.AreEqual(expected, result);
		}
	}
}
