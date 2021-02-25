// ******************************************************************
// Copyright � 2015-2018 nventive inc. All rights reserved.
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
// 
// This file is based on the work from https://github.com/praeclarum/Ooui
// 
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Transactions;
using Microsoft.Build.Framework;
using Microsoft.Win32.SafeHandles;
using Mono.Cecil;
using Mono.CompilerServices.SymbolWriter;
using Newtonsoft.Json.Linq;
using Uno.Wasm.Bootstrap.Extensions;

namespace Uno.Wasm.Bootstrap
{
	public partial class ShellTask_v0
	{
		/// <summary>
		/// Applies a temporary workaround for https://github.com/mono/mono/issues/19824
		/// </summary>
		private string? TransformAOTProfile()
		{
			var profilePath = AotProfile?.FirstOrDefault()?.GetMetadata("FullPath");

			if (profilePath != null)
			{
				var reader = new Mono.Profiler.Aot.ProfileReader();
				Mono.Profiler.Aot.ProfileData profile;
				using (FileStream stream = File.OpenRead(profilePath))
				{
					profile = reader.ReadAllData(stream);
				}

				var excludedMethodsList = AOTProfileExcludedMethods
					.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
					.ToList();

				var excludedAssemblies = MixedModeExcludedAssembly?.ToDictionary(i => i.ItemSpec, i => i.ItemSpec)
					?? new Dictionary<string, string>();

				// LoadIntoBufferAsync uses exception filtering
				excludedMethodsList.AddRange(DefaultAOTProfileExcludedMethods);

				TryDumpProfileMethods(profile, "AOTProfileDump.Original.txt");

				var excludedMethods = excludedMethodsList.Select(e => new Regex(e)).ToList();

				var q = from m in profile.Methods
						where !excludedMethods.Any(e => e.Match(m.Type.FullName + '.' + m.Name).Success)
							&& !excludedAssemblies.ContainsKey(m.Type.Module.Name)
						select m;

				profile.Methods = q.ToArray();

				TryDumpProfileMethods(profile, "AOTProfileDump.Filtered.txt");

				var writer = new Mono.Profiler.Aot.ProfileWriter();

				var outputFile = Path.Combine(IntermediateOutputPath, "aot-filtered.profile");
				using (var outStream = File.Create(outputFile))
				{
					writer.WriteAllData(outStream, profile);
				}

				return outputFile;
			}

			return profilePath;
		}

		private IEnumerable<string> DefaultAOTProfileExcludedMethods =>
			new[]
			{
				@"HttpContent\.LoadIntoBufferAsync", // https://github.com/mono/mono/issues/19824

				@"ManifestBasedResourceGroveler\.InternalGetSatelliteAssembly", // https://github.com/dotnet/runtime/issues/45698

				@"System\.Reflection\.Assembly\.GetExecutingAssembly", // https://github.com/dotnet/runtime/issues/47996
				@"System\.RuntimeType\.GetType",
				@"System\.RuntimeTypeHandle\.internal_from_name",
				@"System\.RuntimeTypeHandle\.GetTypeByName",
				@"System\.Type\.GetType",
				@"System\.Runtime\.Loader\.AssemblyLoadContext\.InternalLoadFromPath",
				@"System\.Runtime\.Loader\.AssemblyLoadContext\.InternalLoadFile",
				@"System\.Runtime\.Loader\.AssemblyLoadContext\.LoadFromAssemblyName",
				@"System\.Reflection\.Assembly\.Load",
				@"System\.Reflection\.Assembly\.InternalLoad",
				@"System\.Reflection\.RuntimeAssembly\.InternalGetSatelliteAssembly",
				@"System\.Reflection\.RuntimeAssembly\.InternalLoad",
			};

		private void TryDumpProfileMethods(Mono.Profiler.Aot.ProfileData profile, string filePath)
		{
			if (GenerateAOTProfileDebugList)
			{
				var sb = new StringBuilder();

				foreach (var method in profile.Methods)
				{
					var genericParameters = string.Join("|", method.GenericInst?.Types.Select(t => t.ToString()) ?? new string[0]);

					sb.AppendLine($"{method.Type.Module.Name};{method.Type.FullName}.{method.Name};{method.GenericInst?.Id};{genericParameters}");
				}

				File.WriteAllText(Path.Combine(IntermediateOutputPath, filePath), sb.ToString());
			}
		}
	}
}
