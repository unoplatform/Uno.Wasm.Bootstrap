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
using Microsoft.Build.Framework;
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
		private readonly List<string> _dependencies = new List<string>();
		private string[] _additionalStyles;

		[Microsoft.Build.Framework.Required]
		public string Assembly { get; set; }

		[Microsoft.Build.Framework.Required]
		public string OutputPath { get; set; }

		[Microsoft.Build.Framework.Required]
		public string IntermediateOutputPath { get; set; }

		[Microsoft.Build.Framework.Required]
		public string MonoWasmSDKPath { get; set; }

		public string PackagerBinPath { get; set; }

		public bool UseFileIntegrity { get; set; } = true;

		public Microsoft.Build.Framework.ITaskItem[] ReferencePath { get; set; }

		public Microsoft.Build.Framework.ITaskItem[] MonoEnvironment { get; set; }

		[Microsoft.Build.Framework.Required]
		public string TargetFrameworkIdentifier { get; set; }

		[Microsoft.Build.Framework.Required]
		public string IndexHtmlPath { get; set; }

		[Microsoft.Build.Framework.Required]
		public string MonoRuntimeExecutionMode { get; set; }

		[Microsoft.Build.Framework.Required]
		public bool MonoILLinker { get; set; }

		/// <summary>
		/// Path override for the mono-wasm SDK folder
		/// </summary>
		public string MonoTempFolder { get; private set; }

		public string AssembliesFileExtension { get; set; } = "clr";

		public Microsoft.Build.Framework.ITaskItem[] Assets { get; set; }

		public Microsoft.Build.Framework.ITaskItem[] LinkerDescriptors { get; set; }

		public Microsoft.Build.Framework.ITaskItem[] MixedModeExcludedAssembly { get; set; }

		public Microsoft.Build.Framework.ITaskItem[] CompressedExtensions { get; set; }

		public bool GenerateCompressedFiles { get; set; }

		[Microsoft.Build.Framework.Required]
		public string RuntimeConfiguration { get; set; }

		[Microsoft.Build.Framework.Required]
		public bool RuntimeDebuggerEnabled { get; set; }

		public string CustomDebuggerPath { get; set; }

		public string CustomLinkerPath { get; set; }

		public string PWAManifestFile { get; set; }

		public override bool Execute()
		{
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
				TryDeployDebuggerProxy();
				HashManagedPath();
				ExtractAdditionalJS();
				ExtractAdditionalCSS();
				GenerateHtml();
				GenerateConfig();
				TryCompressDist();

				return true;
			}
			catch (Exception ex)
			{
				Log.LogError(ex.ToString(), false, true, null);
				return false;
			}
		}

		private void TryCompressDist()
		{
			var hasCompressedExtensions = (CompressedExtensions?.Any() ?? false);

			if (
				!RuntimeDebuggerEnabled
				&& GenerateCompressedFiles
				&& hasCompressedExtensions
			)
			{
				var compressibleExtensions = CompressedExtensions
					.Select(e => e.ItemSpec);

				Log.LogMessage(MessageImportance.Low, $"Compressing {string.Join(", ", compressibleExtensions)}");

				var filesToCompress = compressibleExtensions
					.SelectMany(e => Directory.GetFiles(_distPath, "*" + e, SearchOption.AllDirectories))
					.Where(f => !Path.GetDirectoryName(f).Contains("_compressed_"))
					.Distinct()
					.ToArray();

				CompressFiles(filesToCompress, "gz", GzipCompress);
				CompressFiles(filesToCompress, "br", BrotliCompress);
			}
			else
			{
				Log.LogMessage(MessageImportance.Low,
					$"Compression is disabled (RuntimeDebuggerEnabled:{RuntimeDebuggerEnabled}, " +
					$"GenerateCompressedFiles:{GenerateCompressedFiles}, " +
					$"hasCompressedExtensions:{hasCompressedExtensions})");
			}
		}

		private void CompressFiles(string[] filesToCompress, string method, Action<string, string> compressHandler)
		{
			filesToCompress
				.AsParallel()
				.Select(fileName =>
				{
					var compressedPathBase = Path.Combine(_distPath, "_compressed_" + method);

					var compressedFileName = fileName;
					compressedFileName = compressedFileName.Replace(_distPath, compressedPathBase);

					Directory.CreateDirectory(Path.GetDirectoryName(compressedFileName));

					if (File.Exists(compressedFileName))
					{
						if (File.GetCreationTime(compressedFileName) < File.GetCreationTime(fileName))
						{
							Log.LogMessage(MessageImportance.Low, $"Deleting {compressedFileName} as the source has changed");
							File.Delete(compressedFileName);
						}
					}

					Log.LogMessage($"Compressing {fileName}->{compressedFileName}");

					compressHandler(fileName, compressedFileName);

					return true;
				})
				.ToArray();
		}

		private void GzipCompress(string source, string destination)
		{
			using (var inStream = File.Open(source, FileMode.Open, FileAccess.Read, FileShare.Read))
			using (var compressedFileStream = File.Create(destination))
			using (var compressionStream = new GZipStream(compressedFileStream, CompressionLevel.Optimal))
			{
				inStream.CopyTo(compressionStream);
			}
		}

		private void BrotliCompress(string source, string destination)
		{
			using (var input = File.Open(source, FileMode.Open, FileAccess.Read, FileShare.Read))
			using (var output = File.Create(destination))
			using (var bs = new BrotliSharpLib.BrotliStream(output, CompressionMode.Compress))
			{
				// By default, BrotliSharpLib uses a quality value of 1 and window size of 22 if the methods are not called.
				bs.SetQuality(11);
				/** bs.SetWindow(windowSize); **/
				/** bs.SetCustomDictionary(customDict); **/
				input.CopyTo(bs);

				/* IMPORTANT: Only use the destination stream after closing/disposing the BrotliStream
				   as the BrotliStream must be closed in order to signal that no more blocks are being written
				   for the final block to be flushed out 
				*/
				bs.Dispose();
			}
		}

		private int RunProcess(string executable, string parameters, string workingDirectory = null)
		{
			var p = new Process
			{
				StartInfo =
				{
					UseShellExecute = false,
					RedirectStandardOutput = true,
					RedirectStandardError = true,
					FileName = executable,
					Arguments = parameters
				}
			};

			if (workingDirectory != null)
			{
				p.StartInfo.WorkingDirectory = workingDirectory;
			}

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

		private void TryDeployDebuggerProxy()
		{
			if (RuntimeDebuggerEnabled)
			{
				var sdkName = Path.GetFileName(MonoWasmSDKPath);

				var wasmDebuggerRootPath = Path.Combine(IntermediateOutputPath, "wasm-debugger");
				Directory.CreateDirectory(wasmDebuggerRootPath);

				var debuggerLocalPath = Path.Combine(wasmDebuggerRootPath, sdkName);

				Log.LogMessage(MessageImportance.Low, $"Debugger CustomDebuggerPath:[{CustomDebuggerPath}], {wasmDebuggerRootPath}, {debuggerLocalPath}, {sdkName}");

				if (!Directory.Exists(debuggerLocalPath))
				{
					foreach (var debugger in Directory.GetDirectories(wasmDebuggerRootPath))
					{
						Directory.Delete(debugger, recursive: true);
					}

					Directory.CreateDirectory(debuggerLocalPath);

					string[] debuggerFiles = new[] {
						"Mono.WebAssembly.DebuggerProxy.dll",
						"Mono.Cecil.dll",
						"Mono.Cecil.Mdb.dll",
						"Mono.Cecil.Pdb.dll",
						"Mono.Cecil.Rocks.dll",
					};

					foreach (var debuggerFile in debuggerFiles)
					{
						var sourceBasePath = string.IsNullOrEmpty(CustomDebuggerPath) ? MonoWasmSDKPath : CustomDebuggerPath;

						string sourceFileName = Path.Combine(sourceBasePath, debuggerFile);
						string destFileName = Path.Combine(debuggerLocalPath, debuggerFile);

						if (File.Exists(sourceFileName))
						{
							Log.LogMessage(MessageImportance.High, $"Copying {sourceFileName} -> {destFileName}");
							File.Copy(sourceFileName, destFileName);
						}
						else
						{
							Log.LogMessage($"Skipping [{sourceFileName}] as it does not exist");
						}
					}
				}
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

			var referencePathsParameter = string.Join(" ", _referencedAssemblies.Select(Path.GetDirectoryName).Distinct().Select(r => $"--search-path=\"{r}\""));
			var debugOption = RuntimeDebuggerEnabled ? "--debug" : "";
			string packagerBinPath = string.IsNullOrWhiteSpace(PackagerBinPath) ? Path.Combine(MonoWasmSDKPath, "packager.exe") : PackagerBinPath;

			//
			// Run the packager to create the original layout. The AOT will optionally run over this pass.
			//
			int packagerResults = RunProcess(packagerBinPath, $"{debugOption} {referencePathsParameter} \"{Path.GetFullPath(Assembly)}\"", _distPath);

			if (packagerResults != 0)
			{
				throw new Exception("Failed to generate wasm layout (More details are available in diagnostics mode or using the MSBuild /bl switch)");
			}

			var runtimeExecutionMode = ParseRuntimeExecutionMode();

			if (runtimeExecutionMode == RuntimeExecutionMode.FullAOT || runtimeExecutionMode == RuntimeExecutionMode.InterpreterAndAOT)
			{
				var emsdkPath = Environment.GetEnvironmentVariable("EMSDK");
				if (string.IsNullOrEmpty(emsdkPath))
				{
					throw new InvalidOperationException($"The EMSDK environment variable must be defined. See http://kripken.github.io/emscripten-site/docs/getting_started/downloads.html#installation-instructions");
				}

				var mixedModeExcluded = MixedModeExcludedAssembly
					?.Select(a => a.ItemSpec)
					.ToArray() ?? Array.Empty<string>();

				var mixedModeAotAssembliesParam = mixedModeExcluded.Any() ? "--skip-aot-assemblies=" + string.Join(",", mixedModeExcluded) : "";

				var aotMode = runtimeExecutionMode == RuntimeExecutionMode.InterpreterAndAOT ? $"--aot-interp {mixedModeAotAssembliesParam}" : "--aot";
				var aotOptions = $"{aotMode} --link-mode=all --emscripten-sdkdir=\"{emsdkPath}\" --builddir=\"{workAotPath}\"";

				var aotPackagerResult = RunProcess(packagerBinPath, $"{debugOption} {aotOptions} {referencePathsParameter} \"{Path.GetFullPath(Assembly)}\"", _distPath);

				if (aotPackagerResult != 0)
				{
					throw new Exception("Failed to generate wasm layout (More details are available in diagnostics mode or using the MSBuild /bl switch)");
				}

				var ninjaResult = RunProcess("ninja", "", workAotPath);

				if (ninjaResult != 0)
				{
					throw new Exception("Failed to generate AOT layout (More details are available in diagnostics mode or using the MSBuild /bl switch)");
				}
			}
			else
			{
				//
				// Run the IL Linker on the interpreter based output, as the packager does not yet do it.
				//
				if (
					MonoILLinker
					&& !string.IsNullOrEmpty(CustomLinkerPath)
				)
				{
					string linkerInput = Path.Combine(IntermediateOutputPath, "linker-in");
					if (Directory.Exists(linkerInput))
					{
						Directory.Delete(linkerInput, true);
					}

					Directory.Move(_managedPath, linkerInput);
					Directory.CreateDirectory(_managedPath);

					var assemblyPath = Path.Combine(linkerInput, Path.GetFileName(Assembly));
					var bindingsPath = Path.Combine(linkerInput, "WebAssembly.Bindings.dll");

					var linkerPath = Path.Combine(Path.Combine(CustomLinkerPath, "linker"), "monolinker.exe");

					int linkerResults = RunProcess(
						linkerPath,
						$"-out \"{_managedPath}\" --verbose -b true -l none --exclude-feature com --exclude-feature remoting -a \"{assemblyPath}\" -a \"{bindingsPath}\" -c link -p copy \"WebAssembly.Bindings\" -d \"{_managedPath}\"",
						_managedPath
					   );

					if (linkerResults != 0)
					{
						throw new Exception("Failed to execute the linker");
					}

					//
					// The linker removes files after the mono-config.js file has been 
					// generated by the packager. Synchronize the list with the actual list.
					//
					var deletedFiles = Directory
						.GetFiles(linkerInput)
						.Select(Path.GetFileName)
						.Except(Directory
							.GetFiles(_managedPath)
							.Select(Path.GetFileName)
						);

					string monoConfigFilePath = Path.Combine(_distPath, "mono-config.js");
					var monoConfig = File.ReadAllText(monoConfigFilePath);

					foreach (var deletedFile in deletedFiles)
					{
						Log.LogMessage($"Removing linker deleted file [{deletedFile}] from mono-config.js");
						monoConfig = monoConfig
							.Replace($"\"{deletedFile}\",", "")
							.Replace($"\"{deletedFile}\"", "");
					}

					File.WriteAllText(monoConfigFilePath, monoConfig);
				}
			}
		}

		private RuntimeExecutionMode ParseRuntimeExecutionMode()
		{
			if (Enum.TryParse<RuntimeExecutionMode>(MonoRuntimeExecutionMode, out var runtimeExecutionMode))
			{
				Log.LogMessage(MessageImportance.Low, $"MonoRuntimeExecutionMode={MonoRuntimeExecutionMode}");
			}
			else
			{
				throw new NotSupportedException($"The MonoRuntimeExecutionMode {MonoRuntimeExecutionMode} is not supported");
			}

			return runtimeExecutionMode;
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
			_bclPath = Path.Combine(MonoWasmSDKPath, "wasm-bcl", "wasm");
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

					relativePath = relativePath.Replace("wwwroot" + Path.DirectorySeparatorChar, "");

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

			return from asmPath in _referencedAssemblies.Concat(new[] { Assembly, GetType().Assembly.Location })
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

				var config = new StringBuilder();

				var (monoWasmFileName, monoWasmSize, totalAssembliesSize, assemblyFiles, filesIntegrity) = GetFilesDetails();
				var assembliesSize = string.Join(
					",",
					assemblyFiles.Select(ass => $"\"{ass.fileName}\":{ass.length}"));
				var filesIntegrityStr = string.Join(
					",",
					filesIntegrity.Select(f => $"\"{f.fileName}\":\"{f.integrity}\""));

				config.AppendLine($"config.uno_remote_managedpath = \"{ Path.GetFileName(_managedPath) }\";");
				config.AppendLine($"config.uno_dependencies = [{dependencies}];");
				config.AppendLine($"config.uno_main = \"[{entryPoint.DeclaringType.Module.Assembly.Name.Name}] {entryPoint.DeclaringType.FullName}:{entryPoint.Name}\";");
				config.AppendLine($"config.assemblyFileExtension = \"{AssembliesFileExtension}\";");
				config.AppendLine($"config.mono_wasm_runtime = \"{monoWasmFileName}\";");
				config.AppendLine($"config.mono_wasm_runtime_size = {monoWasmSize};");
				config.AppendLine($"config.assemblies_with_size = {{{assembliesSize}}};");
				config.AppendLine($"config.files_integrity = {{{filesIntegrityStr}}};");
				config.AppendLine($"config.total_assemblies_size = {totalAssembliesSize};");

				config.AppendLine($"config.environmentVariables = config.environmentVariables || {{}};");

				if (MonoEnvironment != null)
				{
					foreach (var env in MonoEnvironment)
					{
						config.AppendLine($"config.environmentVariables[\"{env.ItemSpec}\"] = \"{env.GetMetadata("Value")}\";");
					}
				}

				w.Write(config.ToString());
			}
		}

		private static readonly SHA384Managed _sha384 = new SHA384Managed();

		private (string monoWasmFileName, long monoWasmSize, long totalAssembliesSize, (string fileName, long length)[] assemblyFiles, (string fileName, string integrity)[] filesIntegrity) GetFilesDetails()
		{
			const string monoWasmFileName = "mono.wasm";

			var monoWasmFilePathAndName = Path.Combine(_distPath, monoWasmFileName);
			var monoWasmSize = new FileInfo(monoWasmFilePathAndName).Length;

			var assemblyPathAndFiles = Directory
				.EnumerateFiles(_managedPath, "*." + AssembliesFileExtension, SearchOption.TopDirectoryOnly)
				.ToArray();

			var assemblyFiles = assemblyPathAndFiles
				.Select(f =>
				{
					var fi = new FileInfo(f);
					return (fileName: fi.Name, length: fi.Length);
				})
				.ToArray();

			var totalAssembliesSize = assemblyFiles.Sum(fi => fi.length);

			(string fileName, string integrity)[] filesIntegrity;
			if (UseFileIntegrity)
			{
				var integrities = new List<(string fileName, string integrity)>();

				void AddFileIntegrity(string filePath)
				{
					var bytes = File.ReadAllBytes(filePath);
					var hash = _sha384.ComputeHash(bytes);
					var integrity = Convert.ToBase64String(hash);
					integrities.Add((Path.GetFileName(filePath), $"sha384-{integrity}"));
				}

				AddFileIntegrity(monoWasmFilePathAndName);

				foreach (var f in assemblyPathAndFiles)
				{
					AddFileIntegrity(f);
				}


				filesIntegrity = integrities.ToArray();
			}
			else
			{
				filesIntegrity = new (string fileName, string integrity)[0];
			}

			return (monoWasmFileName, monoWasmSize, totalAssembliesSize, assemblyFiles, filesIntegrity);
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
