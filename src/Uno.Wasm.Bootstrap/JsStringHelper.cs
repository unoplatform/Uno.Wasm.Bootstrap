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

using System.Text;

namespace Uno.Wasm.Bootstrap
{
	internal static class JsStringHelper
	{
		internal static string EscapeJsString(string value)
		{
			var escaped = new StringBuilder(value.Length);

			foreach (var c in value)
			{
				switch (c)
				{
					case '\\':
						escaped.Append("\\\\");
						break;
					case '"':
						escaped.Append("\\\"");
						break;
					case '\r':
						escaped.Append("\\r");
						break;
					case '\n':
						escaped.Append("\\n");
						break;
					case '\t':
						escaped.Append("\\t");
						break;
					case '\b':
						escaped.Append("\\b");
						break;
					case '\f':
						escaped.Append("\\f");
						break;
					case '\v':
						escaped.Append("\\v");
						break;
					case '\0':
						escaped.Append("\\0");
						break;
					case '\u2028':
						escaped.Append("\\u2028");
						break;
					case '\u2029':
						escaped.Append("\\u2029");
						break;
					default:
						if (char.IsControl(c))
						{
							escaped.Append("\\u");
							escaped.Append(((int)c).ToString("x4"));
						}
						else
						{
							escaped.Append(c);
						}
						break;
				}
			}

			return escaped.ToString();
		}
	}
}
