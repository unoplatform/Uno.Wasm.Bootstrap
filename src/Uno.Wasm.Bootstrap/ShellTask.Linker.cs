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
			var references = ReferencePath.Split(';').Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
			_referencedAssemblies = new List<string>();
			foreach (var r in references)
			{
				var name = Path.GetFileName(r);
				if (_bclAssemblies.ContainsKey(name))
				{
					_referencedAssemblies.Add(_bclAssemblies[name]);
				}
				else
				{
					_referencedAssemblies.Add(r);
				}
			}

			var asmPath = Path.GetFullPath(Assembly);

			var pipeline = GetLinkerPipeline();
			using (var context = new LinkContext(pipeline))
			{
				context.CoreAction = AssemblyAction.Link;
				context.UserAction = AssemblyAction.Link;

				// Disabled until we can actually use symbols, and that 
				// the rewriter does not fail for a memory allocation error.
				// context.SymbolReaderProvider = new DefaultSymbolReaderProvider(true);
				// context.SymbolWriterProvider = new DefaultSymbolWriterProvider();
				// context.LinkSymbols = true;
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

				pipeline.AddStepAfter(typeof(LoadReferencesStep), new LoadI18nAssemblies(I18nAssemblies.None));

				foreach (var dll in Directory.GetFiles(_managedPath, "*.dll"))
				{
					File.Delete(dll);
				}

				pipeline.Process(context);
			}

			RenameFiles("dll");
			RenameFiles("exe");

			_linkedAsmPaths = Directory.GetFiles(_managedPath, "*.clrdll")
				.Concat(Directory.GetFiles(_managedPath, "*.clrexe"))
				.OrderBy(x => Path.GetFileName(x))
				.ToList();
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
				string destDirName = Path.Combine(Path.GetDirectoryName(dllFile), Path.GetFileNameWithoutExtension(dllFile) + ".clr" + extension);

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
