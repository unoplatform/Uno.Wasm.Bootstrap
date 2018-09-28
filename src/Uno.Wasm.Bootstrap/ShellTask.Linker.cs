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
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Text;
using Microsoft.Build.Utilities;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Linker;
using Mono.Linker.Steps;

namespace Uno.Wasm.Bootstrap
{
	public partial class ShellTask_v0
	{
		void LinkAssemblies()
		{
			_referencedAssemblies = new List<string>();
			if (ReferencePath != null)
			{
				foreach (var r in ReferencePath)
				{
					var name = Path.GetFileName(r.ItemSpec);
					if (
						_bclAssemblies.ContainsKey(name)

						// NUnitLite is a particular case, as it is distributed
						// as part of the mono runtime BCL, which prevents the nuget
						// package from overriding it. We exclude it here, and cache the
						// proper assembly in the resolver farther below, so that it gets 
						// picked up first.
						&& name != "nunitlite.dll"
					)
					{
						_referencedAssemblies.Add(_bclAssemblies[name]);
					}
					else
					{
						_referencedAssemblies.Add(r.ItemSpec);
					}
				}
			}

			var asmPath = Path.GetFullPath(Assembly);

			var pipeline = GetLinkerPipeline();
			using (var context = new LinkContext(pipeline))
			{
				context.CoreAction = AssemblyAction.Link;
				context.UserAction = AssemblyAction.Link;

				if (RuntimeDebuggerEnabled)
				{
					// Disabled until we can actually use symbols, and that 
					// the rewriter does not fail for a memory allocation error.
					context.SymbolReaderProvider = new DefaultSymbolReaderProvider(true);
					context.SymbolWriterProvider = new DefaultSymbolWriterProvider();
					context.LinkSymbols = true;
				}

				context.Logger = new LinkerLogger(Log);
				context.LogMessages = true;
				context.KeepTypeForwarderOnlyAssemblies = true;
				context.OutputDirectory = _managedPath;

				pipeline.PrependStep(new ResolveFromAssemblyStep(asmPath, ResolveFromAssemblyStep.RootVisibility.Any));

				var refdirs = _referencedAssemblies.Select(x => Path.GetDirectoryName(x)).Distinct().ToList();
				refdirs.Insert(0, Path.Combine(_bclPath, "Facades"));
				refdirs.Insert(0, _bclPath);
				foreach (var d in refdirs.Distinct())
				{
					context.Resolver.AddSearchDirectory(d);
				}

				var nunitlitePath = _referencedAssemblies.FirstOrDefault(s => s.EndsWith("nunitlite.dll", StringComparison.OrdinalIgnoreCase));
				if (!string.IsNullOrWhiteSpace(nunitlitePath))
				{
					Log.LogMessage("Found NUnitLite in references, overriding path to use referenced instead of mono-wasm provided one");
					context.Resolver.CacheAssembly(AssemblyDefinition.ReadAssembly(nunitlitePath, context.ReaderParameters));
				}

				pipeline.AddStepAfter(typeof(LoadReferencesStep), new LoadI18nAssemblies(I18nAssemblies.None));

				foreach (var dll in Directory.GetFiles(_managedPath, "*.dll"))
				{
					File.Delete(dll);
				}

				pipeline.Process(context);
			}

			RenameFiles("dll");

			_linkedAsmPaths = Directory.GetFiles(_managedPath, "*." + AssembliesFileExtension)
				.OrderBy(x => Path.GetFileName(x))
				.ToList();

			if (RuntimeDebuggerEnabled)
			{
				_linkedAsmPaths = _linkedAsmPaths.Concat(
						Directory.GetFiles(_managedPath, "*.pdb")
						// Required because of this 
						.Where(f => !Path.GetFileNameWithoutExtension(f).Equals("mscorlib", StringComparison.OrdinalIgnoreCase))
						.OrderBy(x => Path.GetFileName(x))

					).ToList();
			}
		}

		/// <summary>
		/// Renames the files to avoid quarantine by antivirus software such as Symantec, 
		/// which are quite present in the enterprise space.
		/// </summary>
		/// <param name="extension">The extension to rename</param>
		private void RenameFiles(string extension)
		{
			foreach (var dllFile in Directory.GetFiles(_managedPath, "*." + extension))
			{
				string destDirName = Path.Combine(Path.GetDirectoryName(dllFile), Path.GetFileNameWithoutExtension(dllFile) + "." + AssembliesFileExtension);

				Log.LogMessage($"Renaming {dllFile} to {destDirName}");
				Directory.Move(dllFile, destDirName);
			}
		}

		Pipeline GetLinkerPipeline()
		{
			var p = new Pipeline();
			p.AppendStep(new LoadReferencesStep());
			p.AppendStep(new PreserveUsingAttributesStep(_bclAssemblies.Values.Select(Path.GetFileNameWithoutExtension)));
			p.AppendStep(new PreserveTypeConverters());
			p.AppendStep(new BlacklistStep());
			p.AppendStep(new ExplicitBlacklistStep(LinkerDescriptors?.Select(l => l.ItemSpec)));
			p.AppendStep(new TypeMapStep());
			p.AppendStep(new MarkStep());
			p.AppendStep(new SweepStep());
			p.AppendStep(new CleanStep());
			p.AppendStep(new RegenerateGuidStep());
			p.AppendStep(new OutputStep());
			return p;
		}

		class LinkerLogger : ILogger
		{
			private TaskLoggingHelper log;

			public LinkerLogger(TaskLoggingHelper log) => this.log = log;

			public void LogMessage(MessageImportance importance, string message, params object[] values)
			{
				switch (importance)
				{
					case MessageImportance.High:
						log.LogMessage(Microsoft.Build.Framework.MessageImportance.High, message, values);
						break;
					case MessageImportance.Normal:
						log.LogMessage(Microsoft.Build.Framework.MessageImportance.Normal, message, values);
						break;
					case MessageImportance.Low:
						log.LogMessage(Microsoft.Build.Framework.MessageImportance.Low, message, values);
						break;
				}
			}
		}
	}
}
