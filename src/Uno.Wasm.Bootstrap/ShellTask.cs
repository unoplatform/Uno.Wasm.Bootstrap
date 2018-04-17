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
	public partial class ShellTask_v0 : Microsoft.Build.Utilities.Task
	{
		private const string SdkUrl = "https://jenkins.mono-project.com/job/test-mono-mainline-webassembly/62/label=highsierra/Azure/processDownloadRequest/62/highsierra/sdks/wasm/mono-wasm-ddf4e7be31b.zip";

		private string _distPath;
		private string _managedPath;
		private string _bclPath;
		private List<string> _linkedAsmPaths;
		private List<string> _referencedAssemblies;
		private Dictionary<string, string> _bclAssemblies;
		private string _sdkPath;
		private List<string> _additionalScripts = new List<string>();
		private string[] _additionalStyles;

		[Microsoft.Build.Framework.Required]
		public string Assembly { get; set; }

		[Microsoft.Build.Framework.Required]
		public string OutputPath { get; set; }

		public string ReferencePath { get; set; }

		public Microsoft.Build.Framework.ITaskItem[] Assets { get; set; }

		public Microsoft.Build.Framework.ITaskItem[] LinkerDescriptors { get; set; }

		[Microsoft.Build.Framework.Required]
		public string RuntimeConfiguration { get; set; }

		[Microsoft.Build.Framework.Required]
		public bool RuntimeDebugLogging { get; set; }

		public override bool Execute()
		{
			try
			{
				InstallSdk();
				GetBcl();
				CreateDist();
				CopyContent();
				CopyRuntime();
				LinkAssemblies();
				ExtractAdditionalJS();
				ExtractAdditionalCSS();
				GenerateHtml();
				return true;
			}
			catch (Exception ex)
			{
				Log.LogErrorFromException(ex, false, true, null);
				return false;
			}
		}

		private void InstallSdk()
		{
			var sdkName = Path.GetFileNameWithoutExtension(new Uri(SdkUrl).AbsolutePath.Replace('/', Path.DirectorySeparatorChar));
			Log.LogMessage("SDK: " + sdkName);
			_sdkPath = Path.Combine(Path.GetTempPath(), sdkName);
			Log.LogMessage("SDK Path: " + _sdkPath);

			if (Directory.Exists(_sdkPath))
			{
				return;
			}

			var client = new WebClient();
			var zipPath = _sdkPath + ".zip";
			Log.LogMessage($"Downloading {sdkName} to {zipPath}");
			client.DownloadFile(SdkUrl, zipPath);

			ZipFile.ExtractToDirectory(zipPath, _sdkPath);
			Log.LogMessage($"Extracted {sdkName} to {_sdkPath}");
		}

		private void GetBcl()
		{
			_bclPath = Path.Combine(_sdkPath, "bcl");
			var reals = Directory.GetFiles(_bclPath, "*.dll");
			var facades = Directory.GetFiles(Path.Combine(_bclPath, "Facades"), "*.dll");
			var allFiles = reals.Concat(facades);
			_bclAssemblies = allFiles.ToDictionary(x => Path.GetFileName(x));
		}

		private void CreateDist()
		{
			var outputPath = Path.GetFullPath(OutputPath);
			_distPath = Path.Combine(outputPath, "dist");
			_managedPath = Path.Combine(_distPath, "managed");
			Directory.CreateDirectory(_managedPath);
		}

		private void CopyRuntime()
		{
			var runtimePath = Path.Combine(_sdkPath, RuntimeConfiguration.ToLower());

			foreach (var sourceFile in Directory.EnumerateFiles(runtimePath))
			{
				var dest = Path.Combine(_distPath, Path.GetFileName(sourceFile));
				Log.LogMessage($"Runtime {sourceFile} -> {dest}");
				File.Copy(sourceFile, dest, true);
			}

			File.Copy(Path.Combine(_sdkPath, "server.py"), Path.Combine(_distPath, "server.py"), true);
		}

		private void CopyContent()
		{
			if (Assets != null)
			{
				var runtimePath = Path.Combine(_sdkPath, RuntimeConfiguration.ToLower());

				foreach (var sourceFile in Assets)
				{
					var baseSourceFile = sourceFile.GetMetadata("DefiningProjectDirectory");

					Directory.CreateDirectory(Path.Combine(_distPath, Path.GetDirectoryName(sourceFile.ToString())));

					var dest = Path.Combine(_distPath, sourceFile.ItemSpec);
					var fullSourcePath = Path.Combine(baseSourceFile, sourceFile.ItemSpec);
					Log.LogMessage($"ContentFile {fullSourcePath} -> {dest}");
					File.Copy(fullSourcePath, dest, true);
				}
			}
		}

		class Logger : ILogger
		{
			private TaskLoggingHelper log;

			public Logger(TaskLoggingHelper log) => this.log = log;

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
				context.Logger = new Logger(Log);
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

			_linkedAsmPaths = Directory.GetFiles(_managedPath, "*.dll")
				.Concat(Directory.GetFiles(_managedPath, "*.exe"))
				.OrderBy(x => Path.GetFileName(x))
				.ToList();
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

		private void ExtractAdditionalJS()
		{
			var q = EnumerateResources("js", "WasmDist")
				.Concat(EnumerateResources("js", "WasmScripts"));

			q.AsParallel().ForAll(res =>
			{
				if (res.name != "uno-bootstrap.js")
				{
					_additionalScripts.Add(res.name);
				}

				CopyResourceToOutput(res.name, res.resource);

				Log.LogMessage($"Additional JS {res.name}");
			});
		}

		private void ExtractAdditionalCSS()
		{
			var q = EnumerateResources("css", "WasmCSS");

			_additionalStyles = q.AsParallel().Select(res => {
				using (var srcs = res.resource.GetResourceStream())
				{
					CopyResourceToOutput(res.name, res.resource);

					Log.LogMessage($"Additional CSS {res.name}");
				}

				return res.name;
			})
			.ToArray();
		}

		private void CopyResourceToOutput(string name, EmbeddedResource resource)
		{
			var dest = Path.Combine(_distPath, name);

			using (var srcs = resource.GetResourceStream())
			{
				using (var dests = new FileStream(dest, FileMode.Create, FileAccess.Write))
				{
					srcs.CopyTo(dests);
				}
			}
		}

		private IEnumerable<(string name, EmbeddedResource resource)> EnumerateResources(string extension, string folder)
		{
			var fullExtension = "." + extension;
			var fullFolder = "." + folder + ".";

			return from asmPath in _referencedAssemblies.Concat(new[] { Assembly, this.GetType().Assembly.Location })
				   let asm = AssemblyDefinition.ReadAssembly(asmPath)
				   from res in asm.MainModule.Resources.OfType<EmbeddedResource>()
				   where res.Name.EndsWith(fullExtension)
				   where res.Name.Contains(fullFolder)
				   select (
					name: res.Name.Substring(res.Name.IndexOf(fullFolder) + fullFolder.Length),
					resource: res
					);
		}

		private MethodDefinition DiscoverEntryPoint()
		{
			var asm = AssemblyDefinition.ReadAssembly(Assembly);

			if (asm?.EntryPoint is MethodDefinition def)
			{
				return def;
			}

			throw new Exception($"{Path.GetFileName(Assembly)} is missing an entry point");
		}

		private void GenerateHtml()
		{
			var htmlPath = Path.Combine(_distPath, "index.html");

			var entryPoint = DiscoverEntryPoint();

			using (var w = new StreamWriter(htmlPath, false, new UTF8Encoding(false)))
			{
				string indexHtmlResName = $"{GetType().Assembly.GetName().Name}.Templates.Index.html";
				var stream = this.GetType().Assembly.GetManifestResourceStream(indexHtmlResName);

				if (stream == null)
				{
					throw new InvalidOperationException($"Unable to find {indexHtmlResName}");
				}

				using (var sr = new StreamReader(stream))
				{
					var html = sr.ReadToEnd();
					var assemblies = string.Join(", ", _linkedAsmPaths.Select(x => $"\"{Path.GetFileName(x)}\""));

					html = html.Replace("$(ASSEMBLIES_LIST)", assemblies);
					html = html.Replace("$(MAIN_ASSEMBLY_NAME)", entryPoint.DeclaringType.Module.Assembly.Name.Name);
					html = html.Replace("$(MAIN_NAMESPACE)", entryPoint.DeclaringType.Namespace);
					html = html.Replace("$(MAIN_TYPENAME)", entryPoint.DeclaringType.Name);
					html = html.Replace("$(MAIN_METHOD)", entryPoint.Name);
					html = html.Replace("$(ENABLE_RUNTIMEDEBUG)", RuntimeDebugLogging.ToString().ToLower());

					var scripts = string.Join("\r\n", _additionalScripts.Select(s => $"<script defer type=\"text/javascript\" src=\"{s}\"></script>"));
					html = html.Replace("$(ADDITIONAL_SCRIPTS)", scripts);

					var styles = string.Join("\r\n", _additionalStyles.Select(s => $"<link rel=\"stylesheet\" type=\"text/css\" href=\"{s}\" />"));
					html = html.Replace("$(ADDITIONAL_CSS)", styles);

					w.Write(html);
				}


				Log.LogMessage($"HTML {htmlPath}");
			}
		}
	}
}
