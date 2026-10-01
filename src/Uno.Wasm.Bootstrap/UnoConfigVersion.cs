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

using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Uno.Wasm.Bootstrap;

/// <summary>
/// Versions the references to <c>uno-config.js</c> so it can't be served stale from a cache.
/// </summary>
/// <remarks>
/// <c>uno-config.js</c> lives in the <c>package_&lt;hash&gt;</c> folder, which hosts cache as immutable, but its
/// content changes with every build (it names the fingerprinted <c>dotnet.js</c>) while the package hash does not.
/// <c>index.html</c> (never cached as immutable) loads <c>uno-bootstrap.js?v=&lt;version&gt;</c>, and the
/// bootstrapper forwards that version to its <c>uno-config.js</c> import, giving each config its own URL. The
/// service worker imports <c>uno-config.js</c> directly, so it gets the version too.
/// </remarks>
public static class UnoConfigVersion
{
	private static readonly Regex _references = new(
		@"(?<=[/""'])(?<file>uno-(?:bootstrap|config)\.js)(?:\?v=[A-Za-z0-9]*)?(?=[""'])",
		RegexOptions.CultureInvariant);

	/// <summary>First 12 hex characters of the SHA-256 of the config file.</summary>
	public static string Compute(byte[] configContent)
	{
		using var sha = SHA256.Create();
		var hash = sha.ComputeHash(configContent);

		var builder = new StringBuilder(12);
		for (var i = 0; i < 6; i++)
		{
			builder.Append(hash[i].ToString("x2"));
		}

		return builder.ToString();
	}

	/// <summary>Sets <c>?v=<paramref name="version"/></c> on every quoted <c>uno-bootstrap.js</c> and <c>uno-config.js</c> reference.</summary>
	public static string Apply(string content, string version)
		=> _references.Replace(content, m => $"{m.Groups["file"].Value}?v={version}");
}
