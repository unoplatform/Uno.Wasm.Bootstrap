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
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Mono.Cecil;

namespace Uno.Wasm.Bootstrap
{
	public partial class ShellTask_v0 : Microsoft.Build.Utilities.Task
	{
		private string _distPath;
		private string _managedPath;
		private string _bclPath;
		private List<string> _referencedAssemblies;
		private Dictionary<string, string> _bclAssemblies;
		private List<string> _dependencies = new List<string>();
		private string[] _additionalStyles;

		[Microsoft.Build.Framework.Required]
		public string Assembly { get; set; }

		[Microsoft.Build.Framework.Required]
		public string OutputPath { get; set; }

		[Microsoft.Build.Framework.Required]
		public string IntermediateOutputPath { get; set; }

		[Microsoft.Build.Framework.Required]
		public string MonoWasmSDKPath { get; set; }

		[Microsoft.Build.Framework.Required]
		public string PackagerBinPath { get; set; }

		public Microsoft.Build.Framework.ITaskItem[] ReferencePath { get; set; }

		[Microsoft.Build.Framework.Required]
		public string TargetFrameworkIdentifier { get; set; }

		[Microsoft.Build.Framework.Required]
		public string IndexHtmlPath { get; set; }

		[Microsoft.Build.Framework.Required]
		public bool MonoAOT { get; set; }

		/// <summary>
		/// Path override for the mono-wasm SDK folder
		/// </summary>
		public string MonoTempFolder { get; private set; }

		public string AssembliesFileExtension { get; set; } = "clr";

		public Microsoft.Build.Framework.ITaskItem[] Assets { get; set; }

		public Microsoft.Build.Framework.ITaskItem[] LinkerDescriptors { get; set; }

		[Microsoft.Build.Framework.Required]
		public string RuntimeConfiguration { get; set; }

		[Microsoft.Build.Framework.Required]
		public bool RuntimeDebuggerEnabled { get; set; }

		public string PWAManifestFile { get; set; }

		public override bool Execute()
		{
			var t = typeof(Mono.Options.Command);
			var t2 = typeof(Mono.Cecil.ArrayType);

			try
			{
				if (TargetFrameworkIdentifier != ".NETStandard")
				{
					Log.LogWarning($"The package Uno.Wasm.Bootstrap is not supported for the current project ({Assembly}), skipping dist generation.");
					return true;
				}

				// Debugger.Launch();

				GetBcl();
				CreateDist();
				CopyContent();
				CopyRuntime();
				RunPackager();
				HashManagedPath();
				ExtractAdditionalJS();
				ExtractAdditionalCSS();
				GenerateHtml();
				GenerateConfig();
				return true;
			}
			catch (Exception ex)
			{
				Log.LogError(ex.ToString(), false, true, null);
				return false;
			}
		}

		private int RunProcess(string executable, string parameters, string workingDirectory = null)
		{
			var p = new Process();
			p.StartInfo.WorkingDirectory = workingDirectory;
			p.StartInfo.UseShellExecute = false;
			p.StartInfo.RedirectStandardOutput = true;
			p.StartInfo.RedirectStandardError = true;
			p.StartInfo.FileName = executable;
			p.StartInfo.Arguments = parameters;

			Log.LogMessage($"Running [{p.StartInfo.WorkingDirectory}]: {p.StartInfo.FileName} {p.StartInfo.Arguments}");

			p.OutputDataReceived += (s, e) => { if (e.Data != null) { Log.LogMessage(e.Data); } };
			p.ErrorDataReceived += (s, e) => { if (e.Data != null) { Log.LogError(e.Data); } };

			if (p.Start())
			{
				p.BeginOutputReadLine();
				p.BeginErrorReadLine();
				p.WaitForExit();
				var exitCore = p.ExitCode;
				p.Close();

				return exitCore;
			}
			else
			{
				throw new Exception($"Failed to start [{executable}]");
			}
		}

		private void RunPackager()
		{
			BuildReferencedAssembliesList();

			var workAotPath = Path.Combine(IntermediateOutputPath, "workAot");

			if (Directory.Exists(workAotPath))
			{
				Directory.Delete(workAotPath, true);
			}

			Directory.CreateDirectory(workAotPath);

			var referencePathsParameter = string.Join(" ", _referencedAssemblies.Select(Path.GetDirectoryName).Distinct().Select(r => $"\"--search-path={r}\""));

			int r1 = RunProcess(PackagerBinPath, $"{referencePathsParameter} {Path.GetFullPath(Assembly)}", _distPath);

			if(r1 != 0)
			{
				throw new Exception("Failed to generate wasm layout");
			}

			if (MonoAOT)
			{
				var emsdkPath = Environment.GetEnvironmentVariable("EMSDK");
				if (string.IsNullOrEmpty(emsdkPath))
				{
					throw new InvalidOperationException($"The EMSDK environment variable must be defined. See http://kripken.github.io/emscripten-site/docs/getting_started/downloads.html#installation-instructions");
				}

				var debugOption = this.RuntimeDebuggerEnabled ? "--debug" : "";
				var aotOption = this.MonoAOT ? $"--aot --emscripten-sdkdir=\"{emsdkPath}\" --builddir=\"{workAotPath}\"" : "";

				int r2 = RunProcess(PackagerBinPath, $"{debugOption} {aotOption} {referencePathsParameter} {Path.GetFullPath(Assembly)}", _distPath);

				if(r2 != 0)
				{
					throw new Exception("Failed to generate wasm layout");
				}

				int r3 = RunProcess("ninja", "", workAotPath);

				if (r3 != 0)
				{
					throw new Exception("Failed to generate AOT layout");
				}
			}
		}

		private void BuildReferencedAssembliesList()
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
		}

		private void HashManagedPath()
		{
			var hashFunction = SHA1.Create();

			IEnumerable<byte> ComputeHash(string file)
			{
				using (var s = File.OpenRead(file))
				{
					return hashFunction.ComputeHash(s);
				}
			}

			var allBytes = Directory.GetFiles(_managedPath)
				.OrderBy(s => s)
				.Select(ComputeHash)
				.SelectMany(h => h)
				.ToArray();

			var hash = string.Join("", hashFunction.ComputeHash(allBytes).Select(b => b.ToString("x2")));

			var oldManagedPath = _managedPath;
			_managedPath = _managedPath + "-" + hash;

			if (Directory.Exists(_managedPath))
			{
				Directory.Delete(_managedPath, true);
			}

			Directory.Move(oldManagedPath, _managedPath);

			RenameFiles("dll");
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

		private void GetBcl()
		{
			_bclPath = Path.Combine(MonoWasmSDKPath, "bcl");
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
			var runtimePath = Path.Combine(MonoWasmSDKPath, RuntimeConfiguration.ToLower());

			foreach (var sourceFile in Directory.EnumerateFiles(runtimePath))
			{
				var dest = Path.Combine(_distPath, Path.GetFileName(sourceFile));
				Log.LogMessage($"Runtime {sourceFile} -> {dest}");
				File.Copy(sourceFile, dest, true);
			}

			File.Copy(Path.Combine(MonoWasmSDKPath, "server.py"), Path.Combine(_distPath, "server.py"), true);
		}

		private void CopyContent()
		{
			if (Assets != null)
			{
				var runtimePath = Path.Combine(MonoWasmSDKPath, RuntimeConfiguration.ToLower());

				foreach (var sourceFile in Assets)
				{
					(string fullPath, string relativePath) GetFilePaths()
					{
						if (sourceFile.GetMetadata("Link") is string link && !string.IsNullOrEmpty(link))
						{
							// This case is mainly for shared projects
							return (sourceFile.ItemSpec, link);
						}
						else if (sourceFile.GetMetadata("FullPath") is string fullPath && File.Exists(fullPath))
						{
							// This is fore files added explicitly through other targets (e.g. Microsoft.TypeScript.MSBuild)
							return (fullPath, sourceFile.ToString());
						}
						else
						{
							// This is for project-local defined content
							var baseSourceFile = sourceFile.GetMetadata("DefiningProjectDirectory");

							return (Path.Combine(baseSourceFile, sourceFile.ItemSpec), sourceFile.ToString());
						}
					}

					(var fullSourcePath, var relativePath) = GetFilePaths();

					Directory.CreateDirectory(Path.Combine(_distPath, Path.GetDirectoryName(relativePath)));

					var dest = Path.Combine(_distPath, relativePath);
					Log.LogMessage($"ContentFile {fullSourcePath} -> {dest}");
					File.Copy(fullSourcePath, dest, true);
				}
			}
		}

		private void ExtractAdditionalJS()
		{
			var q = EnumerateResources("js", "WasmDist")
				.Concat(EnumerateResources("js", "WasmScripts"));

			foreach (var (name, source, resource) in q)
			{
				if (source.Name.Name != GetType().Assembly.GetName().Name)
				{
					_dependencies.Add(name);
				}

				CopyResourceToOutput(name, resource);

				Log.LogMessage($"Additional JS {name}");
			}
		}

		private void ExtractAdditionalCSS()
		{
			var q = EnumerateResources("css", "WasmCSS");

			foreach (var (name, source, resource) in q)
			{
				using (var srcs = resource.GetResourceStream())
				{
					CopyResourceToOutput(name, resource);

					Log.LogMessage($"Additional CSS {name}");
				}
			}

			_additionalStyles = q
				.Select(res => res.name)
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

		private IEnumerable<(string name, AssemblyDefinition source, EmbeddedResource resource)> EnumerateResources(string extension, string folder)
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
					source: asm,
					resource: res
					);
		}

		private string GetMonoTempPath()
			=> string.IsNullOrEmpty(MonoTempFolder) ? Path.GetTempPath() : MonoTempFolder;

		private MethodDefinition DiscoverEntryPoint()
		{
			var asm = AssemblyDefinition.ReadAssembly(Assembly);

			if (asm?.EntryPoint is MethodDefinition def)
			{
				return def;
			}

			throw new Exception($"{Path.GetFileName(Assembly)} is missing an entry point. Add <OutputType>Exe</OutputType> in the project file and a static main.");
		}

		private void GenerateConfig()
		{
			var unoConfigJsPath = Path.Combine(_distPath, "uno-config.js");

			using (var w = new StreamWriter(unoConfigJsPath, false, new UTF8Encoding(false)))
			{
				var dependencies = string.Join(", ", _dependencies.Select(x => $"\"{Path.GetFileNameWithoutExtension(x)}\""));
				var entryPoint = DiscoverEntryPoint();

				var config =
					$"config.uno_remote_managedpath = \"{ Path.GetFileName(_managedPath) }\";" +
					$"config.uno_dependencies = [{dependencies}];" +
					$"config.uno_main = \"[{entryPoint.DeclaringType.Module.Assembly.Name.Name}] {entryPoint.DeclaringType.FullName}:{entryPoint.Name}\";" +
					$"config.assemblyFileExtension = \"{AssembliesFileExtension}\";"
					;

				w.Write(config);
			}
		}

		private void GenerateHtml()
		{
			var htmlPath = Path.Combine(_distPath, "index.html");

			using (var w = new StreamWriter(htmlPath, false, new UTF8Encoding(false)))
			{
				using (var reader = new StreamReader(IndexHtmlPath))
				{
					var html = reader.ReadToEnd();

					var styles = string.Join("\r\n", _additionalStyles.Select(s => $"<link rel=\"stylesheet\" type=\"text/css\" href=\"{s}\" />"));
					html = html.Replace("$(ADDITIONAL_CSS)", styles);

					var extraBuilder = new StringBuilder();
					if (!string.IsNullOrWhiteSpace(PWAManifestFile))
					{
						extraBuilder.AppendLine($"<link rel=\"manifest\" href=\"{PWAManifestFile}\" />");
					}

					html = html.Replace("$(ADDITIONAL_HEAD)", extraBuilder.ToString());

					w.Write(html);


					Log.LogMessage($"HTML {htmlPath}");
				}

			}
		}
	}
}
