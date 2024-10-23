// ******************************************************************
// Copyright � 2015-2022 Uno Platform inc. All rights reserved.
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
using System.Net.Http.Headers;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Transactions;
using System.Xml.Linq;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Microsoft.Win32.SafeHandles;
using Mono.Cecil;
using Mono.CompilerServices.SymbolWriter;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Uno.Wasm.Bootstrap.Extensions;

namespace Uno.Wasm.Bootstrap
{
	public partial class ShellTask_v0 : Microsoft.Build.Utilities.Task
	{
		private const string WasmScriptsFolder = "WasmScripts";
		private const string DeployMetadataName = "UnoDeploy";

		private static readonly string _wwwroot = "wwwroot" + Path.DirectorySeparatorChar;

		private ShellMode _shellMode;
		private UTF8Encoding _utf8Encoding = new UTF8Encoding(false);
		private string _remoteBasePackagePath = ".";
		private readonly List<string> _dependencies = new List<string>();
		private List<AssemblyDefinition>? _resourceSearchList;
		private List<string> _referencedAssemblies = new List<string>();
		private string[]? _additionalStyles;
		private string _intermediateAssetsPath = "";
		private string[]? _contentExtensionsToExclude;

		[Required]
		public string AssemblyName { get; private set; } = "";

		[Microsoft.Build.Framework.Required]
		public string IndexHtmlPath { get; set; } = "";

		[Microsoft.Build.Framework.Required]
		public string Assembly { get; set; } = "";

		[Microsoft.Build.Framework.Required]
		public string CurrentProjectPath { get; set; } = "";

		public Microsoft.Build.Framework.ITaskItem[]? Assets { get; set; }

		public Microsoft.Build.Framework.ITaskItem[] EmbeddedResources { get; set; } = [];

		[Microsoft.Build.Framework.Required]
		public string IntermediateOutputPath { get; set; } = "";
		public Microsoft.Build.Framework.ITaskItem[]? MonoEnvironment { get; set; }

		public string? PWAManifestFile { get; set; }

		public string EmscriptenVersion { get; set; } = "";

		public string? ContentExtensionsToExclude { get; set; }

		public string CSPConfiguration { get; set; } = "";

		public string WebAppBasePath { get; set; } = "./";

		public bool GenerateAOTProfile { get; set; }

		public bool EnableThreads { get; set; }

		public Microsoft.Build.Framework.ITaskItem[]? ReferencePath { get; set; }

		[Microsoft.Build.Framework.Output]
		public Microsoft.Build.Framework.ITaskItem[]? StaticWebContent { get; set; } = [];

		[Microsoft.Build.Framework.Output]
		public Microsoft.Build.Framework.ITaskItem[]? NativeFileReference { get; set; } = [];

		public override bool Execute()
		{
			IntermediateOutputPath = TryConvertLongPath(IntermediateOutputPath);
			_intermediateAssetsPath = Path.Combine(IntermediateOutputPath, "unowwwrootassets");
			Directory.CreateDirectory(_intermediateAssetsPath);

			try
			{
				ParseProperties();
				BuildReferencedAssembliesList();
				CopyContent();
				GenerateBitcodeFiles();
				ExtractAdditionalJS();
				ExtractAdditionalCSS();
				GenerateIndexHtml();
				GenerateConfig();
			}
			finally
			{
				Cleanup();
			}

			return true;
		}

		private void ParseProperties()
		{
			_contentExtensionsToExclude =
				ContentExtensionsToExclude
					?.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
				?? new string[0];

			Log.LogMessage($"Ignoring content files with following extensions:\n\t{string.Join("\n\t", _contentExtensionsToExclude)}");
		}

		private void Cleanup()
		{
			if (_resourceSearchList is not null)
			{
				foreach (var res in _resourceSearchList)
				{
					res.Dispose();
				}
			}
		}

		private (string fullPath, string relativePath) GetFilePaths(ITaskItem item)
		{
			// This is for project-local defined content
			var baseSourceFile = item.GetMetadata("DefiningProjectDirectory");

			if (item.GetMetadata("Link") is { } link && !string.IsNullOrEmpty(link))
			{
				var fullPath = Path.IsPathRooted(item.ItemSpec) ? item.ItemSpec : Path.Combine(baseSourceFile, item.ItemSpec);

				// This case is mainly for shared projects and files out of the baseSourceFile path
				return (fullPath, link);
			}
			else if (item.GetMetadata("FullPath") is { } fullPath && File.Exists(fullPath))
			{
				var sourceFilePath = item.ItemSpec;

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
				return (Path.Combine(baseSourceFile, item.ItemSpec), item.ItemSpec);
			}
		}

		private void CopyContent()
		{
			var assets = new List<string>();

			if (Assets != null)
			{
				foreach (var sourceFile in Assets)
				{
					var (fullSourcePath, relativePath) = GetFilePaths(sourceFile);

					// Files in "wwwroot" folder will get deployed to root by default
					var defaultDeployMode = relativePath.Contains(_wwwroot) ? DeployMode.None : DeployMode.Package;
					var deployModeSource = "Default";

					var matchedExtension = _contentExtensionsToExclude
						.FirstOrDefault(x => relativePath.EndsWith(x, StringComparison.OrdinalIgnoreCase));
					if (matchedExtension != null)
					{
						defaultDeployMode = DeployMode.None;
						deployModeSource = "Excluded extension";
					}

					relativePath = PathHelper.FixupPath(relativePath).Replace(_wwwroot, "");

					if (relativePath.StartsWith(WasmScriptsFolder))
					{
						// Skip by default the WasmScript folder files that may
						// have been added as content files.
						// This can happen when running the TypeScript compiler.
						defaultDeployMode = DeployMode.None;
					}

					var deployToRootMetadata = sourceFile.GetMetadata(DeployMetadataName);

					if (Enum.TryParse<DeployMode>(deployToRootMetadata, out var deployMode))
					{
						deployModeSource = "Metadata";
					}
					else
					{
						deployMode = defaultDeployMode;
					}

					var dest = Path.Combine(_intermediateAssetsPath, relativePath);

					if (deployMode != DeployMode.None)
					{
						// Add the file to the package assets manifest
						assets.Add(relativePath.Replace(Path.DirectorySeparatorChar, '/'));

						AddStaticAsset(relativePath, fullSourcePath);
					}

					Log.LogMessage($"ContentFile {fullSourcePath} -> {dest ?? "<null>"} [Mode={deployMode} / by {deployModeSource}]");
				}
			}

			var assetsFilePath = Path.Combine(_intermediateAssetsPath, "uno-assets.txt");
			File.WriteAllLines(assetsFilePath, assets);
			AddStaticAsset(Path.GetFileName(assetsFilePath), assetsFilePath);
		}

		private void FileCopy(string source, string dest, bool overwrite = false)
		{
			var sourceFileName = PathHelper.FixupPath(source);
			var destFileName = PathHelper.FixupPath(dest);

			try
			{
				File.Copy(sourceFileName, destFileName, overwrite);
			}
			catch (Exception ex)
			{
				Log.LogError($"Failed to copy {sourceFileName} to {destFileName}: {ex.Message}");
				throw;
			}
		}

		private void ExtractAdditionalJS()
		{
			BuildResourceSearchList();

			var q = EnumerateResources("js", "WasmDist")
				.Concat(EnumerateResources("js", WasmScriptsFolder));

			foreach (var (name, source, resource) in q)
			{
				if (source.Name.Name != Path.GetFileNameWithoutExtension(Assembly))
				{
					_dependencies.Add(name);

					CopyResourceToOutput(name, resource);

					Log.LogMessage($"Additional JS {name}");
				}
			}

			foreach (var projectResource in EmbeddedResources)
			{
				var (fullSourcePath, relativePath) = GetFilePaths(projectResource);

				if (fullSourcePath.Contains("WasmScripts"))
				{
					var scriptName = Path.GetFileName(fullSourcePath);

					Log.LogMessage($"Embedded resources JS {scriptName}");

					_dependencies.Add(scriptName);
					AddStaticAsset(scriptName, fullSourcePath);
				}
			}
		}

		private void ExtractAdditionalCSS()
		{
			BuildResourceSearchList();

			var additionalStyles = new List<string>();

			foreach (var (name, source, resource) in EnumerateResources("css", "WasmCSS"))
			{
				if (source.Name.Name != Path.GetFileNameWithoutExtension(Assembly))
				{
					using (var srcs = resource.GetResourceStream())
					{
						additionalStyles.Add(name);

						CopyResourceToOutput(name, resource);

						Log.LogMessage($"Additional CSS {name}");
					}
				}
			}

			foreach (var projectResource in EmbeddedResources)
			{
				var (fullSourcePath, relativePath) = GetFilePaths(projectResource);

				if (fullSourcePath.Contains("WasmCSS"))
				{
					var cssName = Path.GetFileName(fullSourcePath);

					Log.LogMessage($"Embedded CSS {cssName}");

					additionalStyles.Add(cssName);
					AddStaticAsset(cssName, fullSourcePath);
				}
			}

			_additionalStyles = additionalStyles.ToArray();
		}

		private void CopyResourceToOutput(string name, EmbeddedResource resource)
		{
			var dest = Path.Combine(_intermediateAssetsPath, name);

			using (var srcs = resource.GetResourceStream())
			{
				using (var dests = new FileStream(dest, FileMode.Create, FileAccess.Write))
				{
					srcs.CopyTo(dests);
				}
			}

			AddStaticAsset(name, dest);
		}

		private void AddStaticAsset(string targetPath, string filePath)
		{
			var contentRoot = targetPath.StartsWith(_intermediateAssetsPath)
						? _intermediateAssetsPath
						: Path.GetDirectoryName(filePath);

			TaskItem indexMetadata = new(
				filePath, new Dictionary<string, string>
				{
					["CopyToOutputDirectory"] = "PreserveNewest",
					["ContentRoot"] = contentRoot,
					["Link"] = "wwwroot/" + targetPath,
				});

			StaticWebContent = StaticWebContent.Concat([indexMetadata]).ToArray();
		}

		private void BuildResourceSearchList()
		{
			if (_resourceSearchList == null)
			{
				var sourceList = new List<string> {
					// Add the bootstrapper assembly first, so the css defined there can be overridden.
					GetType().Assembly.Location
				};

				sourceList.AddRange(_referencedAssemblies);

				// Add the main assembly last so it can have a final say
				sourceList.Add(Assembly);

				_resourceSearchList ??= new List<AssemblyDefinition>(
					from asmPath in sourceList.Distinct()
					let asm = ReadAssembly(asmPath)
					where asm != null
					select asm
				);
			}

			AssemblyDefinition? ReadAssembly(string asmPath)
			{
				try
				{
					return AssemblyDefinition.ReadAssembly(asmPath);
				}
				catch (Exception ex)
				{
					Log.LogMessage($"Failed to read assembly {ex}");
					return null;
				}
			}
		}

		private IEnumerable<(string name, AssemblyDefinition source, EmbeddedResource resource)> EnumerateResources(string extension, string folder)
		{
			var fullExtension = "." + extension;
			var fullFolder = "." + folder + ".";

			return from asm in _resourceSearchList
				   from res in asm.MainModule.Resources.OfType<EmbeddedResource>()
				   where res.Name.EndsWith(fullExtension)
				   where res.Name.Contains(fullFolder)
				   select (
					name: res.Name.Substring(res.Name.IndexOf(fullFolder) + fullFolder.Length),
					source: asm,
					resource: res
					);
		}

		private void BuildReferencedAssembliesList()
		{
			if (ReferencePath != null)
			{
				foreach (var referencePath in ReferencePath)
				{
					_referencedAssemblies.Add(referencePath.ItemSpec);
				}
			}
		}

		static string BuildDependencyPath(string dep, string baseLookup)
			=> baseLookup.StartsWith("/")
				? $"\"{baseLookup}{Path.GetFileName(dep)}\""
				: $"\"{baseLookup}{Path.GetFileNameWithoutExtension(dep)}\"";

		private void GenerateConfig()
		{
			var unoConfigJsPath = Path.Combine(_intermediateAssetsPath, "uno-config.js");

			using (var w = new StreamWriter(unoConfigJsPath, false, _utf8Encoding))
			{
				var baseLookup = _shellMode == ShellMode.Node ? "" : $"{WebAppBasePath}{_remoteBasePackagePath}/";
				var dependencies = string.Join(", ", _dependencies
					.Where(d =>
						!d.EndsWith("require.js")
						&& !d.EndsWith("uno-bootstrap.js")
						&& !d.EndsWith("service-worker.js"))
					.Select(dep => BuildDependencyPath(dep, baseLookup)));
				//var entryPoint = DiscoverEntryPoint();

				var config = new StringBuilder();

				//var (monoWasmFileName, monoWasmSize, totalAssembliesSize, assemblyFiles, filesIntegrity) = GetFilesDetails();
				//var assembliesSize = string.Join(
				//	",",
				//	assemblyFiles.Select(ass => $"\"{ass.fileName}\":{ass.length}"));
				//var filesIntegrityStr = string.Join(
				//	",",
				//	filesIntegrity.Select(f => $"\"{f.fileName}\":\"{f.integrity}\""));

				//var enablePWA = !string.IsNullOrEmpty(PWAManifestFile);
				//var offlineFiles = enablePWA ? string.Join(", ", GetPWACacheableFiles().Select(f => $"\".{f}\"")) : "";

				//var emccExportedRuntimeMethodsParams = string.Join(
				//	",",
				//	GetEmccExportedRuntimeMethods().Select(f => $"\'{f}\'"));

				config.AppendLine($"let config = {{}};");
				//config.AppendLine($"config.uno_remote_managedpath = \"{Path.GetFileName(_managedPath)}\";");
				//config.AppendLine($"config.uno_app_base = \"{WebAppBasePath}{_remoteBasePackagePath}\";");
				config.AppendLine($"config.uno_dependencies = [{dependencies}];");
				//config.AppendLine($"config.uno_main = \"{entryPoint.DeclaringType.Module.Assembly.Name.Name}\";");
				//config.AppendLine($"config.assemblyFileExtension = \"{AssembliesFileExtension}\";");
				//config.AppendLine($"config.assemblyFileNameObfuscationMode = \"{_assembliesFileNameObfuscationMode}\";");
				//config.AppendLine($"config.mono_wasm_runtime = \"{monoWasmFileName}\";");
				//config.AppendLine($"config.mono_wasm_runtime_size = {monoWasmSize};");
				//config.AppendLine($"config.assemblies_with_size = {{{assembliesSize}}};");
				//config.AppendLine($"config.files_integrity = {{{filesIntegrityStr}}};");
				//config.AppendLine($"config.total_assemblies_size = {totalAssembliesSize};");
				//config.AppendLine($"config.enable_pwa = {enablePWA.ToString().ToLowerInvariant()};");
				//config.AppendLine($"config.offline_files = ['{WebAppBasePath}', {offlineFiles}];");
				//config.AppendLine($"config.uno_shell_mode = \"{_shellMode}\";");
				//config.AppendLine($"config.emcc_exported_runtime_methods = [{emccExportedRuntimeMethodsParams}];");

				//if (ObfuscateAssemblies)
				//{
				//	config.AppendLine($"config.assemblyObfuscationKey = \"{Encoding.ASCII.GetString(_obfuscationKey)}\";");
				//}

				if (GenerateAOTProfile)
				{
					config.AppendLine($"config.generate_aot_profile = true;");
				}

				config.AppendLine($"config.environmentVariables = config.environmentVariables || {{}};");

				void AddEnvironmentVariable(string name, string value) => config.AppendLine($"config.environmentVariables[\"{name}\"] = \"{value}\";");

				if (MonoEnvironment != null)
				{
					foreach (var env in MonoEnvironment)
					{
						AddEnvironmentVariable(env.ItemSpec, env.GetMetadata("Value"));
					}
				}

				//var isProfiledAOT = UseAotProfile && _runtimeExecutionMode == RuntimeExecutionMode.InterpreterAndAOT;

				//AddEnvironmentVariable("UNO_BOOTSTRAP_MONO_RUNTIME_MODE", _runtimeExecutionMode.ToString());
				//AddEnvironmentVariable("UNO_BOOTSTRAP_MONO_PROFILED_AOT", isProfiledAOT.ToString());
				//AddEnvironmentVariable("UNO_BOOTSTRAP_LINKER_ENABLED", MonoILLinker.ToString());
				//AddEnvironmentVariable("UNO_BOOTSTRAP_DEBUGGER_ENABLED", RuntimeDebuggerEnabled.ToString());
				//AddEnvironmentVariable("UNO_BOOTSTRAP_MONO_RUNTIME_CONFIGURATION", RuntimeConfiguration);
				//AddEnvironmentVariable("UNO_BOOTSTRAP_MONO_RUNTIME_FEATURES", BuildRuntimeFeatures());
				AddEnvironmentVariable("UNO_BOOTSTRAP_APP_BASE", _remoteBasePackagePath);
				AddEnvironmentVariable("UNO_BOOTSTRAP_WEBAPP_BASE_PATH", WebAppBasePath);

				//if (EnableThreads)
				//{
				//	AddEnvironmentVariable("UNO_BOOTSTRAP_MAX_THREADS", PThreadsPoolSize.ToString());
				//}

				//if (ExtraEmccFlags?.Any(f => f.ItemSpec?.Contains("MAXIMUM_MEMORY=4GB") ?? false) ?? false)
				//{
				//	// Detects the use of the 4GB flag: https://v8.dev/blog/4gb-wasm-memory
				//	AddEnvironmentVariable("UNO_BOOTSTRAP_EMSCRIPTEN_MAXIMUM_MEMORY", "4GB");
				//}

				//if (EnableLogProfiler)
				//{
				//	AddEnvironmentVariable("UNO_BOOTSTRAP_LOG_PROFILER_OPTIONS", LogProfilerOptions);
				//}

				config.AppendLine("export { config };");

				w.Write(config.ToString());

				TaskItem indexMetadata = new(
					unoConfigJsPath, new Dictionary<string, string>
					{
						["CopyToOutputDirectory"] = "PreserveNewest",
						["ContentRoot"] = _intermediateAssetsPath,
						["Link"] = "wwwroot/" + Path.GetFileName(unoConfigJsPath),
					});

				StaticWebContent = StaticWebContent.Concat([indexMetadata]).ToArray();
			}
		}

		private void GenerateIndexHtml()
		{
			if (_shellMode != ShellMode.Browser)
			{
				return;
			}

			var indexHtmlOutputPath = Path.Combine(_intermediateAssetsPath, "index.html");

			using var w = new StreamWriter(indexHtmlOutputPath, false, _utf8Encoding);
			using var reader = new StreamReader(IndexHtmlPath);
			var html = reader.ReadToEnd();

			var styles = string.Join("\r\n", _additionalStyles.Select(s => $"<link rel=\"stylesheet\" type=\"text/css\" href=\"{WebAppBasePath}{s}\" />"));
			html = html.Replace("$(ADDITIONAL_CSS)", styles);

			var extraBuilder = new StringBuilder();
			GeneratePWAContent(extraBuilder);
			GenerateCSPMeta(extraBuilder);

			html = html.Replace("$(ADDITIONAL_HEAD)", extraBuilder.ToString());

			// Compatibility after the change from mono.js to dotnet.js
			html = html.Replace("mono.js\"", "dotnet.js\"");
			if (WebAppBasePath != "./")
			{
				html = html.Replace($"\"{WebAppBasePath}", $"\"{WebAppBasePath}{_remoteBasePackagePath}/");
			}
			html = html.Replace($"\"./", $"\"{WebAppBasePath}{_remoteBasePackagePath}/");

			html = html.Replace("$(WEB_MANIFEST)", $"{WebAppBasePath}{Path.GetFileName(PWAManifestFile)}");

			w.Write(html);

			TaskItem indexMetadata = new(
				indexHtmlOutputPath, new Dictionary<string, string>
				{
					["CopyToOutputDirectory"] = "PreserveNewest",
					["ContentRoot"] = _intermediateAssetsPath,
					["Link"] = "wwwroot/index.html",
				});

			StaticWebContent = StaticWebContent.Concat([indexMetadata]).ToArray();

			Log.LogMessage($"HTML {indexHtmlOutputPath}");
		}

		private void GeneratePWAContent(StringBuilder extraBuilder)
		{
			//if (_shellMode != ShellMode.Browser)
			//{
			//	return;
			//}

			//if (!string.IsNullOrWhiteSpace(PWAManifestFile))
			//{
			//	var manifestDocument = JObject.Parse(File.ReadAllText(PWAManifestFile));

			//	extraBuilder.AppendLine($"<link rel=\"manifest\" href=\"$(WEB_MANIFEST)\" />");

			//	// See https://developer.apple.com/library/archive/documentation/AppleApplications/Reference/SafariHTMLRef/Articles/MetaTags.html
			//	extraBuilder.AppendLine($"<meta name=\"apple-mobile-web-app-capable\" content=\"yes\">");

			//	if (manifestDocument["icons"] is JArray array
			//		&& array.Where(v => v["sizes"]?.Value<string>() == "1024x1024").FirstOrDefault() is JToken img)
			//	{
			//		extraBuilder.AppendLine($"<link rel=\"apple-touch-icon\" href=\"{WebAppBasePath}{img["src"]}\" />");
			//	}

			//	if (manifestDocument["theme_color"]?.Value<string>() is string color)
			//	{
			//		extraBuilder.AppendLine($"<meta name=\"theme-color\" content=\"{color}\" />");
			//	}
			//	else
			//	{
			//		extraBuilder.AppendLine($"<meta name=\"theme-color\" content=\"#fff\" />");
			//	}

			//	// Transform the PWA assets
			//	if (manifestDocument["icons"] is JArray icons && !string.IsNullOrWhiteSpace(_remoteBasePackagePath))
			//	{
			//		foreach (var icon in icons)
			//		{
			//			var originalSource = icon["src"]?.Value<string>();

			//			icon["src"] = originalSource switch
			//			{
			//				string s when s.StartsWith("./") => $"{WebAppBasePath}{_remoteBasePackagePath}/" + s.Substring(2),
			//				string s => $"{_remoteBasePackagePath}/" + s,
			//				_ => originalSource
			//			};
			//		}
			//	}

			//	File.WriteAllText(Path.Combine(_distPath, Path.GetFileName(PWAManifestFile)), manifestDocument.ToString());
			//}
		}

		private void GenerateCSPMeta(StringBuilder extraBuilder)
		{
			if (CSPConfiguration is { Length: > 0 } && _shellMode == ShellMode.Browser)
			{
				// See https://developer.apple.com/library/archive/documentation/AppleApplications/Reference/SafariHTMLRef/Articles/MetaTags.html
				extraBuilder.AppendLine($"<meta http-equiv=\"Content-Security-Policy\" content=\"{CSPConfiguration}\" />\r\n");
			}
		}

		private void GenerateBitcodeFiles()
		{
			var bitcodeFiles = Assets
				?.Where(a => a.ItemSpec.EndsWith(".bc") || a.ItemSpec.EndsWith(".a"))
				.Where(a => !bool.TryParse(a.GetMetadata("UnoAotCompile"), out var compile) || compile)
				.Select(a => GetFilePaths(a).fullPath)
				.ToArray()
				?? [];

			List<string> features = new()
			{
				EnableThreads ? "mt" : "st",
				"simd"
			};

			Log.LogMessage(MessageImportance.Low, $"Bitcode files features lookup filter: {string.Join(",", features)}");

			var list = BitcodeFilesSelector.Filter(new(EmscriptenVersion), features.ToArray(), bitcodeFiles);

			NativeFileReference = list.Select(i => new TaskItem(i)).ToArray();
		}

		private string TryConvertLongPath(string path)
			=> Environment.OSVersion.Platform == PlatformID.Win32NT
				&& !string.IsNullOrEmpty(path)
				&& !path.StartsWith(@"\\?\")
				&& Path.IsPathRooted(path)
				&& FileInfoExtensions.PlatformRequiresLongPathNormalization
				? @"\\?\" + path
				: path;
	}
}
