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

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Uno.Wasm.Bootstrap.AOTWrapperSeeding;

/// <summary>
/// Compares the expected interpreter entry wrappers (<see cref="AotWrapperSeedGenerator"/>) with the functions defined in the AOT bitcode.
/// </summary>
internal static class AotWrapperCheck
{
	public sealed record Missing(string Name, int Expected, int Found, string Origin);

	public sealed record Result(
		int GsharedvtInExpected,
		int GsharedvtInFound,
		int InterpInExpected,
		int InterpInFound,
		IReadOnlyList<Missing> MissingWrappers);

	/// <summary>
	/// Returns the name of the function an <c>llvm-nm</c> output line defines, or <see langword="null"/> when it doesn't define a function. The
	/// lines are <c>&lt;address&gt; &lt;type&gt; &lt;name&gt;</c>; the data symbols (<c>info_...</c>) that come with each function are left out.
	/// </summary>
	public static string? ParseFunctionSymbol(string llvmNmLine)
	{
		var parts = llvmNmLine.Split([' '], StringSplitOptions.RemoveEmptyEntries);
		return parts.Length >= 2 && parts[parts.Length - 2] is "t" or "T" ? parts[parts.Length - 1] : null;
	}

	/// <param name="expectedLines">The lines of the expected wrappers file: the wrapper name, a tab, and a method that needs it.</param>
	/// <param name="functionSymbols">The functions defined in the bitcode.</param>
	public static Result Evaluate(IEnumerable<string> expectedLines, IEnumerable<string> functionSymbols)
	{
		var expected = expectedLines
			.Where(l => l.Length > 0)
			.Select(l => l.Split('\t'))
			.GroupBy(p => p[0], StringComparer.Ordinal)
			.Select(g => (Name: g.Key, Count: g.Count(), Origin: g.First().Length > 1 ? g.First()[1] : ""))
			.ToList();

		var gsharedvtIn = new HashSet<string>(StringComparer.Ordinal);
		var interpIn = new HashSet<string>(StringComparer.Ordinal);

		foreach (string symbol in functionSymbols)
		{
			int index = symbol.IndexOf(WrapperSignature.GsharedvtInMarker, StringComparison.Ordinal);

			if (index >= 0)
			{
				gsharedvtIn.Add(symbol.Substring(index));
				continue;
			}

			index = symbol.IndexOf(WrapperSignature.InterpInMarker, StringComparison.Ordinal);

			if (index >= 0)
			{
				interpIn.Add(symbol.Substring(index));
			}
		}

		// get_debug_sym in aot-compiler.c appends _0, _1, ... to interp_in wrapper names that are already taken, so a name counts once per such
		// variant.
		int Found(string name, int count)
		{
			if (name.StartsWith(WrapperSignature.GsharedvtInMarker, StringComparison.Ordinal))
			{
				return gsharedvtIn.Contains(name) ? 1 : 0;
			}

			int found = interpIn.Contains(name) ? 1 : 0;

			for (int suffix = 0; found < count && interpIn.Contains(name + "_" + suffix.ToString(CultureInfo.InvariantCulture)); suffix++)
			{
				found++;
			}

			return found;
		}

		var results = expected.Select(e => (e.Name, e.Count, e.Origin, Found: Found(e.Name, e.Count))).ToList();
		var gsharedvtInResults = results.Where(r => r.Name.StartsWith(WrapperSignature.GsharedvtInMarker, StringComparison.Ordinal)).ToList();
		var interpInResults = results.Where(r => !r.Name.StartsWith(WrapperSignature.GsharedvtInMarker, StringComparison.Ordinal)).ToList();

		// interp_in wrappers first: a missing one aborts the runtime when it builds an IMT slot.
		var missing = interpInResults.Concat(gsharedvtInResults)
			.Where(r => r.Found < r.Count)
			.Select(r => new Missing(r.Name, r.Count, r.Found, r.Origin))
			.ToList();

		return new Result(
			gsharedvtInResults.Sum(r => r.Count),
			gsharedvtInResults.Sum(r => r.Found),
			interpInResults.Sum(r => r.Count),
			interpInResults.Sum(r => r.Found),
			missing);
	}
}
