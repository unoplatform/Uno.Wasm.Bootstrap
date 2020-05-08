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
using Microsoft.Build.Framework;
using Mono.Cecil;
using Newtonsoft.Json.Linq;
using Uno.Wasm.Bootstrap.Extensions;

namespace Uno.Wasm.Bootstrap
{
	public partial class ShellTask_v0 : Microsoft.Build.Utilities.Task
	{
		private const string WasmScriptsFolder = "WasmScripts";
		private readonly char OtherDirectorySeparatorChar = Path.DirectorySeparatorChar == '/' ? '\\' : '/';

		private string _distPath;
		private string _managedPath;
		private string _bclPath;
		private string[] _bitcodeFilesCache;
		private List<string> _referencedAssemblies;
		private Dictionary<string, string> _bclAssemblies;
		private readonly List<string> _dependencies = new List<string>();
		private string[] _additionalStyles;
		private List<AssemblyDefinition> _referencedAssemblyDefinitions;
		private RuntimeExecutionMode _runtimeExecutionMode;
		private ShellMode _shellMode;
		private string _linkerBinPath;

		[Microsoft.Build.Framework.Required]
		public string CurrentProjectPath { get; set; }

		[Microsoft.Build.Framework.Required]
		public string BuildTaskBasePath { get; set; }

		[Microsoft.Build.Framework.Required]
		public string Assembly { get; set; }

		[Microsoft.Build.Framework.Required]
		public string DistPath { get; set; }

		[Microsoft.Build.Framework.Required]
		public string IntermediateOutputPath { get; set; }
		[Microsoft.Build.Framework.Required]
		public string BaseIntermediateOutputPath { get; set; }

		[Microsoft.Build.Framework.Required]
		public string MonoWasmSDKPath { get; set; }

		public string PackagerBinPath { get; set; }

		public bool UseFileIntegrity { get; set; } = true;

		/// <remarks>
		/// Disabled until browsers get smarted about fetch vs. prefetch invocations.
		/// e.g. Chrome downloads files twice.
		/// </remarks>
		public bool GeneratePrefetchHeaders { get; set; } = false;

		public Microsoft.Build.Framework.ITaskItem[] ReferencePath { get; set; }

		public Microsoft.Build.Framework.ITaskItem[] MonoEnvironment { get; set; }

		[Microsoft.Build.Framework.Required]
		public string TargetFrameworkIdentifier { get; set; }

		[Microsoft.Build.Framework.Required]
		public string TargetFramework { get; set; }

		[Microsoft.Build.Framework.Required]
		public string IndexHtmlPath { get; set; }

		[Required]
		public string WasmShellMode { get; set; }

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

		public Microsoft.Build.Framework.ITaskItem[] ExtraEmccFlags { get; set; }

		public bool GenerateCompressedFiles { get; set; }

		public bool ForceUseWSL { get; set; }

		[Microsoft.Build.Framework.Required]
		public string RuntimeConfiguration { get; set; }

		[Microsoft.Build.Framework.Required]
		public bool RuntimeDebuggerEnabled { get; set; }

		public int BrotliCompressionQuality { get; set; } = 7;

		public string CustomDebuggerPath { get; set; }

		public string CustomLinkerPath { get; set; }

		public string PWAManifestFile { get; set; }

		public bool EnableLongPathSupport { get; set; } = true;

		public bool EnableEmccProfiling { get; set; } = false;

		public override bool Execute()
		{
			try
			{
				if (TargetFrameworkIdentifier != ".NETStandard" && TargetFrameworkIdentifier != ".NETCoreApp")
				{
					Log.LogWarning($"The package Uno.Wasm.Bootstrap is not supported for the current project ({Assembly}), skipping dist generation.");
					return true;
				}

				// Debugger.Launch();

				PreloadAssemblies();
				TryEnableLongPathAware();
				ParseProperties();
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
				CleanupDist();
				GenerateConfig();
				MergeConfig();
				TouchServiceWorker();
				TryCompressDist();

				return true;
			}
			catch (Exception ex)
			{
				Log.LogError(ex.ToString(), false, true, null);
				return false;
			}
		}

		private void PreloadAssemblies()
		{
			// Under some circumstances, the assemblies bundled with the bootstrapper do not
			// get loaded properly by .NET Core. This is method forces the loading of those
			// assemblies in order to let the loading find them automatically.

			var path = Path.GetDirectoryName(new Uri(GetType().Assembly.GetName().CodeBase).LocalPath);

			foreach (var file in Directory.GetFiles(path, "*.dll"))
			{
				_ = System.Reflection.Assembly.LoadFile(Path.Combine(path, file));
			}
		}

		private void TryEnableLongPathAware()
		{
			if (EnableLongPathSupport)
			{
				// In some cases, particularly when using the Azure publish task
				// long paths using the "\\?\" prefix is not supported. Fallback on
				// standard paths in such cases.

				try
				{
					var path = TryConvertLongPath(Path.GetFullPath(DistPath));

					DirectoryCreateDirectory(path);
				}
				catch (ArgumentException e)
				{
					Log.LogMessage($"Long path format use failed, falling back to standard path (Error: {e.Message})");
					EnableLongPathSupport = false;
				}
			}

			IntermediateOutputPath = TryConvertLongPath(IntermediateOutputPath);
			BaseIntermediateOutputPath = TryConvertLongPath(BaseIntermediateOutputPath);
			DistPath = TryConvertLongPath(DistPath);
			MonoTempFolder = TryConvertLongPath(MonoTempFolder);
			CurrentProjectPath = TryConvertLongPath(CurrentProjectPath);
			CustomDebuggerPath = TryConvertLongPath(CustomDebuggerPath);
		}


		private void TouchServiceWorker()
		{
			// The service worker file must change to be reloaded properly, add the dist digest
			// as cache trasher.
			var workerFilePath = Path.Combine(_distPath, "service-worker.js");
			var workerBody = File.ReadAllText(workerFilePath);

			workerBody = workerBody.Replace("$(CACHE_KEY)", Path.GetFileName(_managedPath));
			workerBody += $"\r\n\r\n// {Path.GetFileName(_managedPath)}";

			File.WriteAllText(workerFilePath, workerBody);
		}

		/// <summary>
		/// Align paths to fix issues with mixed path
		/// </summary>
		string FixupPath(string path)
			=> path.Replace(OtherDirectorySeparatorChar, Path.DirectorySeparatorChar);

		private void FileCopy(string source, string dest, bool overwrite = false)
		{
			var sourceFileName = FixupPath(source);
			var destFileName = FixupPath(dest);

			try
			{
				File.Copy(sourceFileName, destFileName, overwrite);
			}
			catch (Exception)
			{
				Log.LogError($"Failed to copy {sourceFileName} to {destFileName}");
				throw;
			}
		}

		private void DirectoryCreateDirectory(string directory)
		{
			var directoryName = FixupPath(directory);

			try
			{
				Directory.CreateDirectory(directoryName);
			}
			catch (Exception e)
			{
				Log.LogError($"Failed to create directory [{directoryName}][{directory}]");
				throw;
			}
		}

		private void CleanupDist()
		{
			var unusedFiles = new[] {
				"*.wast",
				"*.bc",
				"*.a",
			};

			foreach (var unusedFile in unusedFiles)
			{
				foreach (var file in Directory.EnumerateFiles(_distPath, unusedFile))
				{
					Log.LogMessage(MessageImportance.Low, $"Removing unused file {file}");
					File.Delete(file);
				}
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
			=> filesToCompress
				.AsParallel()
				.Select(fileName =>
				{
					var compressedPathBase = Path.Combine(_distPath, "_compressed_" + method);

					var compressedFileName = fileName;
					compressedFileName = compressedFileName.Replace(_distPath, compressedPathBase);

					DirectoryCreateDirectory(Path.GetDirectoryName(compressedFileName));

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
				bs.SetQuality(BrotliCompressionQuality);
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

		private bool IsWSLRequired =>
			Environment.OSVersion.Platform == PlatformID.Win32NT
			&& (GetBitcodeFilesParams().Any() || _runtimeExecutionMode != RuntimeExecutionMode.Interpreter || ForceUseWSL);

		private (int exitCode, string output, string error) RunProcess(string executable, string parameters, string workingDirectory = null)
		{
			if(IsWSLRequired)
			{
				var unixPath = AlignPath(executable, escape: true);
				var monoPath = executable.EndsWith(".exe") ? "mono" : "";
				var cwd = workingDirectory != null ? $"cd {AlignPath(workingDirectory, escape: true)} && " : "";

				parameters = $"-c \" {cwd} {monoPath} {unixPath} " + parameters.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
				executable = Path.Combine(Environment.GetEnvironmentVariable("WINDIR"), "sysnative", "bash.exe");

				if (!File.Exists(executable))
				{
					throw new InvalidOperationException($"WSL is required for this build but could not be found. (Searched for [{executable}])");
				}
			}
			else if (RuntimeHelpers.IsNetCore
				&& (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
				|| RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
				)
			)
			{
				parameters = $"{executable} {parameters}";
				executable = "mono";
			}

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

			var output = new StringBuilder();
			var error = new StringBuilder();
			p.OutputDataReceived += (s, e) => { if (e.Data != null) { Log.LogMessage(e.Data); output.Append(e.Data); } };
			p.ErrorDataReceived += (s, e) => { if (e.Data != null) { Log.LogError(e.Data); error.Append(e.Data); } };

			if (p.Start())
			{
				p.BeginOutputReadLine();
				p.BeginErrorReadLine();
				p.WaitForExit();
				var exitCore = p.ExitCode;
				p.Close();

				return (exitCore, output.ToString(), error.ToString());
			}
			else
			{
				throw new Exception($"Failed to start [{executable}]");
			}
		}

		private string AlignPath(string path, bool escape = false)
		{
			var output = IsWSLRequired ? $"`wslpath \"{path.Replace("\\\\?\\", "")}\"`" : path;

			if (escape)
			{
				output = output.Replace("\"", "\\\"");
			}

			return output;
		}

		private void TryDeployDebuggerProxy()
		{
			if (RuntimeDebuggerEnabled)
			{
				var sdkName = Path.GetFileName(MonoWasmSDKPath);

				var wasmDebuggerRootPath = Path.Combine(IntermediateOutputPath, "wasm-debugger");
				DirectoryCreateDirectory(wasmDebuggerRootPath);

				if (!string.IsNullOrEmpty(CustomDebuggerPath))
				{
					CustomDebuggerPath = FixupPath(CustomDebuggerPath);
				}

				var debuggerLocalPath = Path.Combine(wasmDebuggerRootPath, sdkName);

				Log.LogMessage($"Debugger CustomDebuggerPath:[{CustomDebuggerPath}], {wasmDebuggerRootPath}, {debuggerLocalPath}, {sdkName}");

				if (!Directory.Exists(debuggerLocalPath))
				{
					foreach (var debugger in Directory.GetDirectories(wasmDebuggerRootPath))
					{
						Directory.Delete(debugger, recursive: true);
					}

					DirectoryCreateDirectory(debuggerLocalPath);

					var sourceBasePath = FixupPath(string.IsNullOrEmpty(CustomDebuggerPath) ? Path.Combine(MonoWasmSDKPath, "dbg-proxy", "netcoreapp3.0") : CustomDebuggerPath);

					foreach (var debuggerFilePath in Directory.EnumerateFiles(sourceBasePath))
					{
						var debuggerFile = Path.GetFileName(debuggerFilePath);

						string sourceFileName = Path.Combine(sourceBasePath, debuggerFile);
						string destFileName = Path.Combine(debuggerLocalPath, debuggerFile);

						if (File.Exists(sourceFileName))
						{
							Log.LogMessage(MessageImportance.High, $"Copying {sourceFileName} -> {destFileName}");
							FileCopy(sourceFileName, destFileName);
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

			DirectoryCreateDirectory(workAotPath);

			var referencePathsParameter = string.Join(" ", _referencedAssemblies.Select(Path.GetDirectoryName).Distinct().Select(r => $"--search-path=\"{AlignPath(r)}\""));
			var debugOption = RuntimeDebuggerEnabled ? "--debug" : "";
			string packagerBinPath = string.IsNullOrWhiteSpace(PackagerBinPath) ? Path.Combine(MonoWasmSDKPath, "packager.exe") : PackagerBinPath;
			var appDirParm = $"--appdir=\"{AlignPath(_distPath)}\" ";

			//
			// Run the packager to create the original layout. The AOT will optionally run over this pass.
			//
			var packagerResults = RunProcess(packagerBinPath, $"--runtime-config={RuntimeConfiguration} {appDirParm} --zlib {debugOption} {referencePathsParameter} \"{AlignPath(TryConvertLongPath(Path.GetFullPath(Assembly)))}\"", _distPath);

			if (packagerResults.exitCode != 0)
			{
				throw new Exception("Failed to generate wasm layout (More details are available in diagnostics mode or using the MSBuild /bl switch)");
			}

			if (IsRuntimeAOT() || GetBitcodeFilesParams().Any() || IsWSLRequired)
			{
				var emsdkPath = ValidateEmscripten();

				var extraEmccFlags = (ExtraEmccFlags?.Select(f => f.ItemSpec) ?? new string[0]).ToList();
				var packagerParams = new List<string>();

				var mixedModeExcluded = MixedModeExcludedAssembly
					?.Select(a => a.ItemSpec)
					.ToArray() ?? Array.Empty<string>();

				var mixedModeAotAssembliesParam = mixedModeExcluded.Any() ? "--skip-aot-assemblies=" + string.Join(",", mixedModeExcluded) : "";

				var dynamicLibraries = GetDynamicLibrariesParams();
				var dynamicLibraryParams = dynamicLibraries.Any() ? "--pinvoke-libs=" + string.Join(",", dynamicLibraries) : "";

				var bitcodeFiles = GetBitcodeFilesParams();
				var bitcodeFilesParams = dynamicLibraries.Any() ? string.Join(" ", bitcodeFiles.Select(f => $"\"--native-lib={AlignPath(f)}\"")) : "";

				string buildRuntimeFlags()
				{
					switch (_runtimeExecutionMode)
					{
						case RuntimeExecutionMode.FullAOT:
							return "--aot";
						case RuntimeExecutionMode.InterpreterAndAOT:
							return $"--aot-interp {mixedModeAotAssembliesParam}";
						case RuntimeExecutionMode.Interpreter:
							return "";
						default:
							throw new NotSupportedException($"Mode {_runtimeExecutionMode} is not supported");
					}
				}

				var aotMode = buildRuntimeFlags();
				var aotOptions = $"{aotMode} --linker --link-mode=all {dynamicLibraryParams} {bitcodeFilesParams} --emscripten-sdkdir=\"{AlignPath(emsdkPath)}\" --builddir=\"{AlignPath(workAotPath)}\"";

				if (EnableEmccProfiling)
				{
					extraEmccFlags.Add("--profiling");
					packagerParams.Add("--no-native-strip");
				}

				var extraEmccFlagsPararm = string.Join(" ", extraEmccFlags).Replace("\\", "\\\\");

				packagerParams.Add(debugOption);
				packagerParams.Add("--zlib");
				packagerParams.Add("--enable-fs ");
				packagerParams.Add($"--extra-emccflags=\"{extraEmccFlagsPararm} ");
				packagerParams.Add("-lidbfs.js\" ");
				packagerParams.Add(appDirParm);
				packagerParams.Add($"--runtime-config={RuntimeConfiguration} ");
				packagerParams.Add(aotOptions);
				packagerParams.Add(referencePathsParameter);
				packagerParams.Add(AlignPath(Path.GetFullPath(Assembly)));

				var aotPackagerResult = RunProcess(packagerBinPath, string.Join(" ", packagerParams), _distPath);

				if (aotPackagerResult.exitCode != 0)
				{
					throw new Exception("Failed to generate wasm layout (More details are available in diagnostics mode or using the MSBuild /bl switch)");
				}

				var ninjaResult = RunProcess("ninja", "", workAotPath);

				if (ninjaResult.exitCode != 0)
				{
					throw new Exception("Failed to generate AOT layout (More details are available in diagnostics mode or using the MSBuild /bl switch)");
				}
			}
			else
			{
				LinkerSetup();

				//
				// Run the IL Linker on the interpreter based output, as the packager does not yet do it.
				//
				if (
					MonoILLinker
				)
				{
					string linkerInput = Path.Combine(IntermediateOutputPath, "linker-in");
					if (Directory.Exists(linkerInput))
					{
						Directory.Delete(linkerInput, true);
					}

					Directory.Move(_managedPath, linkerInput);
					DirectoryCreateDirectory(_managedPath);

					var assemblyPath = Path.Combine(linkerInput, Path.GetFileName(Assembly));

					var frameworkBindings = new[] {
						"WebAssembly.Bindings.dll",
						"System.Net.Http.WebAssemblyHttpHandler.dll",
						"WebAssembly.Net.WebSockets.dll",
					};

					var bindingsPath = string.Join(" ", frameworkBindings.Select(a => $"-a \"{Path.Combine(linkerInput, a)}\""));

					// Opts should be aligned with the monolinker call in packager.cs, validate for linker_args as well
					var packagerLinkerOpts = $"--deterministic --disable-opt unreachablebodies --exclude-feature com --exclude-feature remoting --exclude-feature etw --used-attrs-only true ";

					var linkerResults = RunProcess(
						_linkerBinPath,
						$"-out \"{_managedPath}\" --verbose -b true -l none {packagerLinkerOpts} -a \"{assemblyPath}\" {bindingsPath} -c link -p copy \"WebAssembly.Bindings\" -d \"{_managedPath}\"",
						_managedPath
					   );

					if (linkerResults.exitCode != 0)
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

		private void LinkerSetup()
		{
			_linkerBinPath = CustomLinkerPath ?? Path.Combine(MonoWasmSDKPath, "wasm-bcl", "wasm_tools", "monolinker.exe");

			var configFilePath = _linkerBinPath + ".config";
			if (!File.Exists(configFilePath))
			{
				var content = @"
<?xml version=""1.0"" encoding=""utf-16"" ?>
<configuration>
  <runtime>
    <AppContextSwitchOverrides value=""Switch.System.IO.UseLegacyPathHandling=false;Switch.System.IO.BlockLongPaths=false"" />
  </runtime>
</configuration>
";
				// Enable long path support for the linker
				File.WriteAllText(configFilePath, content, Encoding.Unicode);
			}
		}

		private bool IsRuntimeAOT() => _runtimeExecutionMode == RuntimeExecutionMode.FullAOT || _runtimeExecutionMode == RuntimeExecutionMode.InterpreterAndAOT;

		private string TryConvertLongPath (string path)
			=> Environment.OSVersion.Platform == PlatformID.Win32NT
				&& !string.IsNullOrEmpty(path)
				&& !path.StartsWith(@"\\?\")
				&& Path.IsPathRooted(path)
				&& EnableLongPathSupport
				&& FileInfoExtensions.PlatformRequiresLongPathNormalization
				? @"\\?\" + path
				: path;

		private string ValidateEmscripten()
		{
			if (Environment.OSVersion.Platform == PlatformID.Win32NT)
			{
				var emsdkBaseFolder = BaseIntermediateOutputPath + $"\\emsdk-{Constants.EmscriptenMinVersion}";

				if(!File.Exists(Environment.GetEnvironmentVariable("WINDIR") + "\\sysnative\\bash.exe"))
				{
					throw new InvalidCastException("The Windows Subsystem for Linux is not installed, please install Ubuntu 18.04 by visiting https://docs.microsoft.com/en-us/windows/wsl/install-win10.");
				}

				// Enable compression for the emsdk folder
				var emsdkBaseFolderRaw = emsdkBaseFolder.Replace(@"\\?\", "");
				if (!Directory.Exists(emsdkBaseFolderRaw))
				{
					Log.LogMessage($"Creating {emsdkBaseFolder}");
					Directory.CreateDirectory(emsdkBaseFolderRaw);
					Process.Start("compact", $"/c \"/s:{emsdkBaseFolder}\"");
				}

				var emscriptenSetupScript = Path.Combine(BuildTaskBasePath, "scripts", "emscripten-setup.sh");

				// Adjust line endings
				AdjustFileLineEndings(emscriptenSetupScript);

				var result = RunProcess(
					emscriptenSetupScript,
					$"\"{BaseIntermediateOutputPath.Replace("\\\\?\\", "").TrimEnd('\\')}\" {Constants.EmscriptenMinVersion}");

				if (result.exitCode == 0)
				{
					return emsdkBaseFolder + $"\\emsdk";
				}

				var dotnetSetupScript = Path.Combine(BuildTaskBasePath, "scripts", "dotnet-setup.sh");
				AdjustFileLineEndings(dotnetSetupScript);

				Log.LogError(
					$"The Windows Subsystem for Linux dotnet environment may not be properly setup, and you may need to run " +
					$"the environment setup script. Open a Windows Command Prompt and run:\n\nbash -c `wslpath \"{dotnetSetupScript}\"`\n\n");

				throw new InvalidOperationException($"Failed to setup WSL environment.");
			}
			else
			{
				var emsdkPath = Environment.GetEnvironmentVariable("EMSDK");
				if (string.IsNullOrEmpty(emsdkPath))
				{
					throw new InvalidOperationException($"The EMSDK environment variable must be defined. See http://kripken.github.io/emscripten-site/docs/getting_started/downloads.html#installation-instructions");
				}

				// Get the version file https://github.com/emscripten-core/emsdk/blob/efc64876db1473312587a3f346be000a733bc16d/emsdk.py#L1698
				var emsdkVersionVersionFile = Path.Combine(emsdkPath, "upstream", "emscripten", "emscripten-version.txt");

				var rawEmsdkVersionVersion = File.Exists(emsdkVersionVersionFile) ? File.ReadAllText(emsdkVersionVersionFile)?.Trim('\"') : "";
				var validVersion = Version.TryParse(rawEmsdkVersionVersion, out var emsdkVersion);

				if (string.IsNullOrWhiteSpace(emsdkPath)
					|| !validVersion
					|| emsdkVersion < Constants.EmscriptenMinVersion)
				{
					throw new InvalidOperationException($"The EMSDK version {emsdkVersion} is not compatible with the current mono SDK. Install {Constants.EmscriptenMinVersion} or later.");
				}

				return emsdkPath;
			}
		}

		private static void AdjustFileLineEndings(string emscriptenSetupScript)
			=> File.WriteAllText(emscriptenSetupScript, File.ReadAllText(emscriptenSetupScript).Replace("\r\n", "\n"));

		private IEnumerable<string> GetDynamicLibrariesParams() =>
			GetBitcodeFilesParams().Select(Path.GetFileNameWithoutExtension);

		private IEnumerable<string> GetBitcodeFilesParams()
		{
			var bitcodeFiles = new[] { "*.bc", "*.a" };

			_bitcodeFilesCache = _bitcodeFilesCache ?? bitcodeFiles
				.SelectMany(b => Directory.EnumerateFiles(_distPath, b, SearchOption.TopDirectoryOnly))
				.ToArray();

			return _bitcodeFilesCache;
		}

		private void ParseProperties()
		{
			ParseEnumProperty(nameof(WasmShellMode), WasmShellMode, out _shellMode);
			ParseEnumProperty(nameof(MonoRuntimeExecutionMode), MonoRuntimeExecutionMode, out _runtimeExecutionMode);
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

			_referencedAssemblyDefinitions = new List<AssemblyDefinition>(
				from asmPath in _referencedAssemblies.Concat(new[] { Assembly, GetType().Assembly.Location })
				select AssemblyDefinition.ReadAssembly(asmPath)
			);
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
			_distPath = TryConvertLongPath(Path.GetFullPath(DistPath));
			_managedPath = Path.Combine(_distPath, "managed");

			Log.LogMessage($"Creating managed path {_managedPath}");
			DirectoryCreateDirectory(_managedPath);
		}

		private void CopyRuntime()
		{
			// Adjust for backward compatibility
			RuntimeConfiguration = RuntimeConfiguration == "release-dynamic" ? "dynamic-release" : RuntimeConfiguration;

			var runtimePath = Path.Combine(MonoWasmSDKPath, "builds", RuntimeConfiguration.ToLower());

			foreach (var sourceFile in Directory.EnumerateFiles(runtimePath))
			{
				var dest = Path.Combine(_distPath, Path.GetFileName(sourceFile));
				Log.LogMessage($"Runtime {sourceFile} -> {dest}");
				FileCopy(sourceFile, dest, true);
			}

			FileCopy(Path.Combine(MonoWasmSDKPath, "server.py"), Path.Combine(_distPath, "server.py"), true);
		}

		private void CopyContent()
		{
			if (Assets != null)
			{
				foreach (var sourceFile in Assets)
				{
					(string fullPath, string relativePath) GetFilePaths()
					{
						// This is for project-local defined content
						var baseSourceFile = sourceFile.GetMetadata("DefiningProjectDirectory");

						if (sourceFile.GetMetadata("Link") is string link && !string.IsNullOrEmpty(link))
						{
							// This case is mainly for shared projects
							return (sourceFile.ItemSpec, link);
						}
						else if (sourceFile.GetMetadata("FullPath") is string fullPath && File.Exists(fullPath))
						{
							var sourceFilePath = sourceFile.ToString();

							if (sourceFilePath.StartsWith(CurrentProjectPath))
							{
								// This is for files added explicitly through other targets (e.g. Microsoft.TypeScript.MSBuild)
								return (fullPath: fullPath, sourceFilePath.Replace(CurrentProjectPath + Path.DirectorySeparatorChar, ""));
							}
							else
							{
								return (fullPath, sourceFilePath);
							}
						}
						else
						{
							return (Path.Combine(baseSourceFile, sourceFile.ItemSpec), sourceFile.ToString());
						}
					}

					(var fullSourcePath, var relativePath) = GetFilePaths();

					relativePath = FixupPath(relativePath).Replace("wwwroot" + Path.DirectorySeparatorChar, "");

					if (!relativePath.StartsWith(WasmScriptsFolder))
					{
						// Skip WasmScript folder files that may have been added as content files.
						// This can happen when running the TypeScript compiler.

						var dest = Path.Combine(_distPath, relativePath);
						DirectoryCreateDirectory(Path.GetDirectoryName(dest));

						Log.LogMessage($"ContentFile {fullSourcePath} -> {dest}");

						FileCopy(fullSourcePath, dest, true);
					}
				}
			}
		}

		private void ExtractAdditionalJS()
		{
			var q = EnumerateResources("js", "WasmDist")
				.Concat(EnumerateResources("js", WasmScriptsFolder));

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

			return from asm in _referencedAssemblyDefinitions
				   from res in asm.MainModule.Resources.OfType<EmbeddedResource>()
				   where res.Name.EndsWith(fullExtension)
				   where res.Name.Contains(fullFolder)
				   select (
					name: res.Name.Substring(res.Name.IndexOf(fullFolder) + fullFolder.Length),
					source: asm,
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

				var enablePWA = !string.IsNullOrEmpty(PWAManifestFile);
				var offlineFiles = enablePWA ? string.Join(", ", GetPWACacheableFiles().Select(f => $"\"{f}\"")) : "";

				var dynamicLibraries = Directory.GetFiles(_distPath, "*.wasm", SearchOption.TopDirectoryOnly)
					.Select(Path.GetFileName)
					.Where(f => !f.Equals("dotnet.wasm"));

				// Note that the "./" is required because mono is requesting files this
				// way, and emscripten is having an issue on second loads for the same
				// logical path: https://github.com/emscripten-core/emscripten/issues/8511
				var sdynamicLibrariesOption = IsRuntimeAOT() ? "" : string.Join(", ", dynamicLibraries.Select(f => $"\"./{f}\""));

				config.AppendLine($"config.uno_remote_managedpath = \"{ Path.GetFileName(_managedPath) }\";");
				config.AppendLine($"config.uno_dependencies = [{dependencies}];");
				config.AppendLine($"config.uno_main = \"[{entryPoint.DeclaringType.Module.Assembly.Name.Name}] {entryPoint.DeclaringType.FullName}:{entryPoint.Name}\";");
				config.AppendLine($"config.assemblyFileExtension = \"{AssembliesFileExtension}\";");
				config.AppendLine($"config.mono_wasm_runtime = \"{monoWasmFileName}\";");
				config.AppendLine($"config.dynamicLibraries = [{sdynamicLibrariesOption}];");
				config.AppendLine($"config.mono_wasm_runtime_size = {monoWasmSize};");
				config.AppendLine($"config.assemblies_with_size = {{{assembliesSize}}};");
				config.AppendLine($"config.files_integrity = {{{filesIntegrityStr}}};");
				config.AppendLine($"config.total_assemblies_size = {totalAssembliesSize};");
				config.AppendLine($"config.enable_pwa = {enablePWA.ToString().ToLowerInvariant()};");
				config.AppendLine($"config.offline_files = ['./', {offlineFiles}];");

				config.AppendLine($"config.environmentVariables = config.environmentVariables || {{}};");

				void AddEnvironmentVariable(string name, string value) => config.AppendLine($"config.environmentVariables[\"{name}\"] = \"{value}\";");

				if (MonoEnvironment != null)
				{
					foreach (var env in MonoEnvironment)
					{
						AddEnvironmentVariable(env.ItemSpec, env.GetMetadata("Value"));
					}
				}

				AddEnvironmentVariable("UNO_BOOTSTRAP_MONO_RUNTIME_MODE", _runtimeExecutionMode.ToString());
				AddEnvironmentVariable("UNO_BOOTSTRAP_LINKER_ENABLED", MonoILLinker.ToString());
				AddEnvironmentVariable("UNO_BOOTSTRAP_DEBUGGER_ENABLED", RuntimeDebuggerEnabled.ToString());
				AddEnvironmentVariable("UNO_BOOTSTRAP_MONO_RUNTIME_CONFIGURATION", RuntimeConfiguration);

				w.Write(config.ToString());
			}
		}

		private void MergeConfig()
		{
			if(_shellMode == ShellMode.Node)
			{
				var tempFile = Path.GetTempFileName();
				try
				{
					var monoJsPath = Path.Combine(_distPath, "dotnet.js");

					using (var fs = new StreamWriter(tempFile))
					{
						fs.Write(File.ReadAllText(Path.Combine(_distPath, "mono-config.js")));
						fs.Write(File.ReadAllText(Path.Combine(_distPath, "uno-config.js")));
						fs.Write(File.ReadAllText(Path.Combine(_distPath, "uno-bootstrap.js")));
						fs.Write(File.ReadAllText(monoJsPath));
					}

					File.Delete(monoJsPath);
					File.Move(tempFile, monoJsPath);

					Log.LogMessage($"Merged config files with dotnet.js");
				}
				finally
				{
					try
					{
						if (File.Exists(tempFile))
						{
							File.Delete(tempFile);
						}
					}
					catch(Exception e)
					{
						Console.WriteLine($"Failed to delete temporary file: {e}");
					}
				}
			}
		}

		private IEnumerable<string> GetPWACacheableFiles()
			=> from file in Directory.EnumerateFiles(_distPath, "*.*", SearchOption.AllDirectories)
			   where !file.EndsWith("web.config", StringComparison.OrdinalIgnoreCase)
			   select file.Replace(_distPath, "").Replace("\\", "/");

		private static readonly SHA384Managed _sha384 = new SHA384Managed();

		private (string monoWasmFileName, long monoWasmSize, long totalAssembliesSize, (string fileName, long length)[] assemblyFiles, (string fileName, string integrity)[] filesIntegrity) GetFilesDetails()
		{
			const string monoWasmFileName = "dotnet.wasm";

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
			if (_shellMode != ShellMode.Browser)
			{
				return;
			}

			var htmlPath = Path.Combine(_distPath, "index.html");

			using (var w = new StreamWriter(htmlPath, false, new UTF8Encoding(false)))
			{
				using (var reader = new StreamReader(IndexHtmlPath))
				{
					var html = reader.ReadToEnd();

					var styles = string.Join("\r\n", _additionalStyles.Select(s => $"<link rel=\"stylesheet\" type=\"text/css\" href=\"./{s}\" />"));
					html = html.Replace("$(ADDITIONAL_CSS)", styles);

					var extraBuilder = new StringBuilder();
					GeneratePWAContent(extraBuilder);
					GeneratePrefetchHeaderContent(extraBuilder);

					html = html.Replace("$(ADDITIONAL_HEAD)", extraBuilder.ToString());

					// Compatibility after the change from mono.js to dotnet.js
					html = html.Replace("mono.js\"", "dotnet.js\"");

					w.Write(html);

					Log.LogMessage($"HTML {htmlPath}");
				}
			}
		}

		private void GeneratePrefetchHeaderContent(StringBuilder extraBuilder)
		{
			if (_shellMode == ShellMode.Browser && GeneratePrefetchHeaders)
			{
				extraBuilder.AppendLine($"<link rel=\"prefetch\" href=\"./dotnet.wasm\" />");

				var distName = Path.GetFileName(_managedPath);
				foreach(var file in Directory.GetFiles(_managedPath, "*.clr", SearchOption.AllDirectories))
				{
					extraBuilder.AppendLine($"<link rel=\"prefetch\" href=\"./{distName}/{Path.GetFileName(file)}\" />");
				}
			}
		}

		private void GeneratePWAContent(StringBuilder extraBuilder)
		{
			if (_shellMode != ShellMode.Browser)
			{
				return;
			}

			if (!string.IsNullOrWhiteSpace(PWAManifestFile))
			{
				var manifestDocument = JObject.Parse(File.ReadAllText(PWAManifestFile));

				extraBuilder.AppendLine($"<link rel=\"manifest\" href=\"./{PWAManifestFile}\" />");
				extraBuilder.AppendLine($"<link rel=\"script\" href=\"./service-worker.js\" />");

				// See https://developer.apple.com/library/archive/documentation/AppleApplications/Reference/SafariHTMLRef/Articles/MetaTags.html
				extraBuilder.AppendLine($"<meta name=\"apple-mobile-web-app-capable\" content=\"yes\">");

				if (manifestDocument["icons"] is JArray array
					&& array.Where(v => v["sizes"]?.Value<string>() == "1024x1024").FirstOrDefault() is JToken img)
				{
					extraBuilder.AppendLine($"<link rel=\"apple-touch-icon\" href=\"./{img["src"].ToString()}\" />");
				}

				if (manifestDocument["theme_color"]?.Value<string>() is string color)
				{
					extraBuilder.AppendLine($"<meta name=\"theme-color\" content=\"{color}\" />");
				}
				else
				{
					extraBuilder.AppendLine($"<meta name=\"theme-color\" content=\"#fff\" />");
				}
			}
		}

		private void ParseEnumProperty<TEnum>(string name, string stringValue, out TEnum value) where TEnum : struct
		{
			if (Enum.TryParse<TEnum>(stringValue, true, out value))
			{
				Log.LogMessage(MessageImportance.Low, $"{name}={value}");
			}
			else
			{
				throw new NotSupportedException($"The {name} {value} is not supported");
			}
		}
	}
}
