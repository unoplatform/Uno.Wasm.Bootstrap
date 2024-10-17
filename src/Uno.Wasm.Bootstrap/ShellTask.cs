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
	public partial class ShellTask_v0 : Task
	{
		private const string WasmScriptsFolder = "WasmScripts";
		private const string DeployMetadataName = "UnoDeploy";
		private const string PackageTag = "%PACKAGE%";

		private static readonly string _wwwroot = "wwwroot" + Path.DirectorySeparatorChar;

		private ShellMode _shellMode;
		private UTF8Encoding _utf8Encoding = new UTF8Encoding(false);
		private readonly List<string> _dependencies = new List<string>();
		private List<AssemblyDefinition>? _resourceSearchList;
		private List<string> _referencedAssemblies = new List<string>();
		private string[]? _additionalStyles;
		private string _intermediateAssetsPath = "";
		private string[]? _contentExtensionsToExclude;
		private RuntimeExecutionMode _runtimeExecutionMode;

		[Required]
		public string AssemblyName { get; private set; } = "";

		[Required]
		public string IndexHtmlPath { get; set; } = "";

		[Required]
		public string Assembly { get; set; } = "";

		[Required]
		public string CurrentProjectPath { get; set; } = "";

		public ITaskItem[]? Assets { get; set; }

		[Required]
		public string WasmShellMode { get; set; } = "";

		public ITaskItem[] ExistingStaticWebAsset { get; set; } = [];

		public ITaskItem[] EmbeddedResources { get; set; } = [];

		[Required]
		public string IntermediateOutputPath { get; set; } = "";

		public ITaskItem[]? MonoEnvironment { get; set; }

		public string? PWAManifestFile { get; set; }

		public ITaskItem[]? EmccFlags { get; set; }

		public ITaskItem[]? EmccExportedRuntimeMethod { get; set; }

		public string? ContentExtensionsToExclude { get; set; }

		public string CSPConfiguration { get; set; } = "";

		public bool Optimize { get; set; }

		public bool EnableTracing { get; set; }

		public bool LoadAllSatelliteResources { get; set; }

		public string AotProfile { get; set; } = "";

		public bool RunAOTCompilation { get; set; }

		public string? RuntimeOptions { get; set; }

		public bool WasmBuildNative { get; set; }

		public bool PublishTrimmed { get; set; }

		public bool RunILLink { get; set; }

		public bool EnableLogProfiler { get; set; }

		public string LogProfilerOptions { get; set; } = "log:alloc,output=output.mlpd";

		public string WebAppBasePath { get; set; } = "./";

		public bool GenerateAOTProfile { get; set; }

		public bool EnableThreads { get; set; }

		public ITaskItem[]? ReferencePath { get; set; }

		[Output]
		public ITaskItem[] StaticWebContent { get; set; } = [];

		[Output]
		public string PackageAssetsFolder { get; set; } = "";

		public override bool Execute()
		{
			IntermediateOutputPath = IntermediateOutputPath;
			_intermediateAssetsPath = Path.Combine(IntermediateOutputPath, "unowwwrootassets");
			Directory.CreateDirectory(_intermediateAssetsPath);

			try
			{
				ParseProperties();
				BuildReferencedAssembliesList();
				CopyContent();
				ExtractAdditionalJS();
				ExtractAdditionalCSS();
				RemoveDuplicateAssets();
				GeneratePackageFolder();
				BuildServiceWorker();
				GenerateEmbeddedJs();
				GenerateIndexHtml();
				GenerateConfig();
				RemoveDuplicateAssets();
			}
			finally
			{
				Cleanup();
			}

			return true;
		}

		private void GeneratePackageFolder()
		{
			IEnumerable<byte> ComputeHash(string file)
			{
				using var hashFunction = SHA1.Create();
				using var s = File.Open(file, FileMode.Open, FileAccess.Read, FileShare.Read);
				return hashFunction.ComputeHash(s);
			}

			var allBytes = StaticWebContent
				.Select(c => c.ItemSpec)
				.AsParallel()
				.OrderBy(s => s)
				.Select(ComputeHash)
				.SelectMany(h => h)
				.ToArray();

			using var hashFunction = SHA1.Create();
			var hash = string.Join("", hashFunction.ComputeHash(allBytes).Select(b => b.ToString("x2")));

			foreach(var staticAsset in StaticWebContent)
			{
				var targetPath = staticAsset.GetMetadata("Link");

				staticAsset.SetMetadata("Link", targetPath.Replace($"wwwroot/{PackageTag}/", $"wwwroot/package_{hash}/"));
			}

			PackageAssetsFolder = $"package_{hash}";
		}

		private void RemoveDuplicateAssets()
		{
			// Remove duplicate assets from the list to be exported.
			// They might have been imported from the build pass.

			var existingAssets = StaticWebContent
				.Where(s => ExistingStaticWebAsset.Any(e => e.ItemSpec == s.ItemSpec || e.GetMetadata("FullPath") == s.GetMetadata("FullPath")))
				.ToArray();

			foreach (var existingAsset in existingAssets)
			{
				Log.LogMessage(MessageImportance.Low, $"Existing asset to remove [{existingAsset.ItemSpec}]");
			}

			// remove existingAssets from StaticWebContent
			StaticWebContent = StaticWebContent
				.Where(s => !existingAssets.Contains(s))
				.ToArray();
		}

		private void ParseProperties()
		{
			ParseEnumProperty(nameof(WasmShellMode), WasmShellMode, out _shellMode);

			_runtimeExecutionMode
				= WasmBuildNative && RunAOTCompilation ? RuntimeExecutionMode.InterpreterAndAOT : RuntimeExecutionMode.Interpreter;

			_contentExtensionsToExclude =
				ContentExtensionsToExclude
					?.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
				?? [];

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

		private void CopyContent()
		{
			var assets = new List<string>();

			if (Assets != null)
			{
				foreach (var sourceFile in Assets)
				{
					var (fullSourcePath, relativePath) = sourceFile.GetFilePaths(Log, CurrentProjectPath);

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

					if (deployMode != DeployMode.None)
					{
						AddStaticAsset(relativePath, fullSourcePath, deployMode);
					}

					Log.LogMessage($"ContentFile {fullSourcePath} -> [Mode={deployMode} / by {deployModeSource}, {relativePath}]");
				}
			}
		}

		private void BuildServiceWorker()
		{
			using var resourceStream = GetType().Assembly.GetManifestResourceStream("Uno.Wasm.Bootstrap.v0.Embedded.service-worker.js");
			using var reader = new StreamReader(resourceStream);

			var worker = TouchServiceWorker(reader.ReadToEnd());
			var memoryStream = new MemoryStream();

			using var writer = new StreamWriter(memoryStream, Encoding.UTF8);
			writer.Write(worker);
			writer.Flush();

			memoryStream.Position = 0;

			CopyStreamToOutput("service-worker.js", memoryStream, DeployMode.Root);
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
				var (fullSourcePath, relativePath) = projectResource.GetFilePaths(Log, CurrentProjectPath);

				string? getPath()
				{
					if (relativePath.Contains(WasmScriptsFolder))
					{
						return relativePath;
					}
					else if (fullSourcePath.Contains(WasmScriptsFolder))
					{
						return fullSourcePath;
					}
					return null;
				}

				if (getPath() is { Length: > 0 } path)
				{
					var scriptName = Path.GetFileName(path);

					Log.LogMessage($"Embedded resources JS {scriptName}");

					_dependencies.Add(scriptName);
					AddStaticAsset(scriptName, fullSourcePath, overrideExisting: true);
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
				var (fullSourcePath, relativePath) = projectResource.GetFilePaths(Log, CurrentProjectPath);

				if (relativePath.Contains("WasmCSS") || fullSourcePath.Contains("WasmCSS"))
				{
					var cssName = Path.GetFileName(fullSourcePath);

					Log.LogMessage($"Embedded CSS {cssName}");

					additionalStyles.Add(cssName);
					AddStaticAsset(cssName, fullSourcePath, overrideExisting: true);
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

		private void CopyStreamToOutput(string name, Stream stream, DeployMode deployMode = DeployMode.Package)
		{
			var dest = Path.Combine(_intermediateAssetsPath, name);

			using (var destStream = new FileStream(dest, FileMode.Create, FileAccess.Write))
			{
				stream.CopyTo(destStream);
			}

			AddStaticAsset(name, dest, deployMode);
		}

		private void AddStaticAsset(
			string targetPath
			, string filePath
			, DeployMode deployMode = DeployMode.Package
			, bool overrideExisting = false)
		{
			var contentRoot = targetPath.StartsWith(_intermediateAssetsPath)
						? _intermediateAssetsPath
						: Path.GetDirectoryName(filePath);

			var packagePath = PackageAssetsFolder == "" ? PackageTag : PackageAssetsFolder;
			var linkBase = deployMode == DeployMode.Root ? "" : $"{packagePath}/";

			TaskItem indexMetadata = new(
				filePath, new Dictionary<string, string>
				{
					["CopyToOutputDirectory"] = "PreserveNewest",
					["ContentRoot"] = contentRoot,
					["Link"] = $"wwwroot/{linkBase}{targetPath}",
				});

			StaticWebContent = StaticWebContent

				// We may be adding duplicate content if a file is present in both Assets and EmbeddedResources.
				// This may happen when generating scripts from TypeScript, where the task includes the result as content.
				.Where(f => overrideExisting ? !(f.ItemSpec == filePath || f.GetMetadata("FullPath") == filePath) : true)

				.Concat([indexMetadata]).ToArray();
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
				var baseLookup = _shellMode == ShellMode.Node ? "" : $"{WebAppBasePath}{PackageAssetsFolder}/";
				var dependencies = string.Join(", ", _dependencies
					.Where(d =>
						!d.EndsWith("require.js")
						&& !d.EndsWith("uno-bootstrap.js")
						&& !d.EndsWith("service-worker.js"))
					.Select(dep => BuildDependencyPath(dep, baseLookup)));

				var config = new StringBuilder();

				var enablePWA = !string.IsNullOrEmpty(PWAManifestFile);

				var sanitizedOfflineFiles = StaticWebContent
					.Select(f => f.GetMetadata("Link")
						.Replace("\\", "/")
						.Replace("wwwroot/", ""))
					.Concat([$"uno-config.js", "_framework/blazor.boot.json", "."]);

				var offlineFiles = enablePWA ? string.Join(", ", sanitizedOfflineFiles.Select(f => $"\"{WebAppBasePath}{f}\"")) : "";

				var emccExportedRuntimeMethodsParams = string.Join(
					",",
					GetEmccExportedRuntimeMethods().Select(f => $"\'{f}\'"));

				var runtimeOptionsSet = string.Join(",", (RuntimeOptions?.Split(' ') ?? []).Select(f => $"\'{f}\'"));

				config.AppendLine($"let config = {{}};");
				config.AppendLine($"config.uno_remote_managedpath = \"_framework\";");
				config.AppendLine($"config.uno_app_base = \"{WebAppBasePath}{PackageAssetsFolder}\";");
				config.AppendLine($"config.uno_dependencies = [{dependencies}];");
				config.AppendLine($"config.uno_runtime_options = [{runtimeOptionsSet}];");
				config.AppendLine($"config.enable_pwa = {enablePWA.ToString().ToLowerInvariant()};");
				config.AppendLine($"config.offline_files = ['{WebAppBasePath}', {offlineFiles}];");
				config.AppendLine($"config.uno_shell_mode = \"{_shellMode}\";");
				config.AppendLine($"config.uno_debugging_enabled = {(!Optimize).ToString().ToLowerInvariant()};");
				config.AppendLine($"config.uno_enable_tracing = {EnableTracing.ToString().ToLowerInvariant()};");
				config.AppendLine($"config.uno_load_all_satellite_resources = {LoadAllSatelliteResources.ToString().ToLowerInvariant()};");
				config.AppendLine($"config.emcc_exported_runtime_methods = [{emccExportedRuntimeMethodsParams}];");

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

				var isProfiledAOT = UseAotProfile && _runtimeExecutionMode == RuntimeExecutionMode.InterpreterAndAOT;

				AddEnvironmentVariable("UNO_BOOTSTRAP_MONO_RUNTIME_MODE", _runtimeExecutionMode.ToString());
				AddEnvironmentVariable("UNO_BOOTSTRAP_MONO_PROFILED_AOT", isProfiledAOT.ToString());
				AddEnvironmentVariable("UNO_BOOTSTRAP_LINKER_ENABLED", (PublishTrimmed && RunILLink).ToString());
				AddEnvironmentVariable("UNO_BOOTSTRAP_DEBUGGER_ENABLED", (!Optimize).ToString());
				AddEnvironmentVariable("UNO_BOOTSTRAP_MONO_RUNTIME_CONFIGURATION", "Release");
				AddEnvironmentVariable("UNO_BOOTSTRAP_MONO_RUNTIME_FEATURES", BuildRuntimeFeatures());
				AddEnvironmentVariable("UNO_BOOTSTRAP_APP_BASE", PackageAssetsFolder);
				AddEnvironmentVariable("UNO_BOOTSTRAP_WEBAPP_BASE_PATH", WebAppBasePath);

				if (EmccFlags?.Any(f => f.ItemSpec?.Contains("MAXIMUM_MEMORY=4GB") ?? false) ?? false)
				{
					// Detects the use of the 4GB flag: https://v8.dev/blog/4gb-wasm-memory
					AddEnvironmentVariable("UNO_BOOTSTRAP_EMSCRIPTEN_MAXIMUM_MEMORY", "4GB");
				}

				if (EnableLogProfiler)
				{
					AddEnvironmentVariable("UNO_BOOTSTRAP_LOG_PROFILER_OPTIONS", LogProfilerOptions);
				}

				config.AppendLine("export { config };");

				w.Write(config.ToString());

				TaskItem indexMetadata = new(
					unoConfigJsPath, new Dictionary<string, string>
					{
						["CopyToOutputDirectory"] = "PreserveNewest",
						["ContentRoot"] = _intermediateAssetsPath,
						["Link"] = $"wwwroot/{PackageAssetsFolder}/" + Path.GetFileName(unoConfigJsPath),
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
				html = html.Replace($"\"{WebAppBasePath}", $"\"{WebAppBasePath}{PackageAssetsFolder}/");
			}
			html = html.Replace($"\"./", $"\"{WebAppBasePath}{PackageAssetsFolder}/");

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
			if (_shellMode != ShellMode.Browser)
			{
				return;
			}

			if (!string.IsNullOrWhiteSpace(PWAManifestFile))
			{
				var manifestDocument = JObject.Parse(File.ReadAllText(PWAManifestFile));

				extraBuilder.AppendLine($"<link rel=\"manifest\" href=\"$(WEB_MANIFEST)\" />");

				extraBuilder.AppendLine($"<meta name=\"mobile-web-app-capable\" content=\"yes\">");

				if (manifestDocument["icons"] is JArray array
					&& array.Where(v => v["sizes"]?.Value<string>() == "1024x1024").FirstOrDefault() is JToken img)
				{
					extraBuilder.AppendLine($"<link rel=\"apple-touch-icon\" href=\"{WebAppBasePath}{img["src"]}\" />");
				}

				if (manifestDocument["theme_color"]?.Value<string>() is string color)
				{
					extraBuilder.AppendLine($"<meta name=\"theme-color\" content=\"{color}\" />");
				}
				else
				{
					extraBuilder.AppendLine($"<meta name=\"theme-color\" content=\"#fff\" />");
				}

				// Transform the PWA assets
				if (manifestDocument["icons"] is JArray icons && !string.IsNullOrWhiteSpace(PackageAssetsFolder))
				{
					foreach (var icon in icons)
					{
						var originalSource = icon["src"]?.Value<string>();

						icon["src"] = originalSource switch
						{
							string s when s.StartsWith("./") => $"{WebAppBasePath}{PackageAssetsFolder}/" + s.Substring(2),
							string s => $"{PackageAssetsFolder}/" + s,
							_ => originalSource
						};
					}
				}

				var pwaManifestFileName = Path.GetFileName(PWAManifestFile);
				var pwaManifestOutputPath = Path.Combine(_intermediateAssetsPath, pwaManifestFileName);
				File.WriteAllText(pwaManifestOutputPath, manifestDocument.ToString());

				AddStaticAsset(Path.GetFileName(PWAManifestFile), pwaManifestOutputPath, DeployMode.Root);
			}
		}

		private void GenerateCSPMeta(StringBuilder extraBuilder)
		{
			if (CSPConfiguration is { Length: > 0 } && _shellMode == ShellMode.Browser)
			{
				// See https://developer.apple.com/library/archive/documentation/AppleApplications/Reference/SafariHTMLRef/Articles/MetaTags.html
				extraBuilder.AppendLine($"<meta http-equiv=\"Content-Security-Policy\" content=\"{CSPConfiguration}\" />\r\n");
			}
		}

		private IEnumerable<string> GetEmccExportedRuntimeMethods()
			=> (EmccExportedRuntimeMethod ?? Array.Empty<ITaskItem>()).Select(m => m.ItemSpec);

		private bool UseAotProfile
			=> !string.IsNullOrEmpty(AotProfile) && _runtimeExecutionMode == RuntimeExecutionMode.InterpreterAndAOT;

		private void GenerateEmbeddedJs()
		{
			if (_shellMode != ShellMode.BrowserEmbedded)
			{
				return;
			}

			var scriptPath = Path.Combine(IntermediateOutputPath, "shell-embedded.js");

			using var w = new StreamWriter(scriptPath, append: false, _utf8Encoding);
			const string javascriptTemplate =
				"""
				(async function () {

					const executingScript = document.currentScript;
					if(!executingScript) {
						console.err("embedded.js MUST be run using a <script> tag in the current version.");
						return;
					}

					const executingScriptAbsolutePath = (new URL(executingScript.src, document.location)).href;

					const absolutePath = (new URL("./$(PACKAGE_PATH)", executingScriptAbsolutePath)).href;

					const styles = [$(STYLES)];

					styles.forEach(s => {
						const scriptElement = document.createElement("link");
						scriptElement.setAttribute("href", `${absolutePath}/${s}`);
						scriptElement.setAttribute("rel", "stylesheet");
						document.head.appendChild(scriptElement);
					});

					document.baseURI = absolutePath;
					document.uno_app_base_override = absolutePath;
					const baseElement = document.createElement("base");
					baseElement.setAttribute("href", absolutePath);
					document.head.appendChild(baseElement);

					const html = "<div id='uno-body' class='container-fluid uno-body'><div class='uno-loader' loading-position='bottom' loading-alert='none'><img class='logo' src='' /><progress></progress><span class='alert'></span></div></div>";

					if(typeof unoRootElement !== 'undefined') {
						unoRootElement.innerHTML = html;
					} else {
						var rootDiv = document.createElement("div");
						rootDiv.innerHTML = html;
						document.body.appendChild(rootDiv);
					}

					const loadScript = s => new Promise((ok, err) => {
						const scriptElement = document.createElement("script");
						scriptElement.setAttribute("src", `${absolutePath}/${s}.js`);
						scriptElement.setAttribute("type", "text/javascript");
						scriptElement.onload = () => ok();
						scriptElement.onerror = () => err("err loading " + s);
						document.head.appendChild(scriptElement);
					});

					// Preload RequireJS dependency
					await loadScript("require");

					// Launch the bootstrapper
					await import(absolutePath + "/uno-bootstrap.js");

					// Yield to the browser to render the splash screen
					await new Promise(r => setTimeout(r, 0));

					// Dispatch the DOMContentLoaded event
					const loadedEvent = new Event("DOMContentLoaded");
					document.dispatchEvent(loadedEvent);
				})();
				""";

			var stylesString = string.Join(",", _additionalStyles?.Select(s => $"\"{Uri.EscapeDataString(s)}\"") ?? Array.Empty<string>());

			var javascript = javascriptTemplate
				.Replace("$(PACKAGE_PATH)", PackageAssetsFolder)
				.Replace("$(STYLES)", stylesString);
			w.Write(javascript);
			w.Flush();

			// Write index.html loading the javascript as a helper
			var htmlPath = Path.Combine(IntermediateOutputPath, "shell-embedded-index.html");
			using var w2 = new StreamWriter(htmlPath, append: false, _utf8Encoding);

			const string html = "<html><body><script src=\"embedded.js\" type=\"text/javascript\"></script>\n";
			w2.Write(html);
			w2.Flush();

			AddStaticAsset("embedded.js", scriptPath, DeployMode.Root);
			AddStaticAsset("index.html", htmlPath, DeployMode.Root);
		}

		private string TouchServiceWorker(string workerBody)
		{
			workerBody = workerBody.Replace("$(CACHE_KEY)", Guid.NewGuid().ToString());
			workerBody = workerBody.Replace("$(REMOTE_BASE_PATH)", PackageAssetsFolder);
			workerBody = workerBody.Replace("$(REMOTE_WEBAPP_PATH)", WebAppBasePath);

			return workerBody;
		}

		private string TryConvertLongPath(string path)
			=> Environment.OSVersion.Platform == PlatformID.Win32NT
				&& !string.IsNullOrEmpty(path)
				&& !path.StartsWith(@"\\?\")
				&& Path.IsPathRooted(path)
				&& FileInfoExtensions.PlatformRequiresLongPathNormalization
				? @"\\?\" + path
				: path;

		private string BuildRuntimeFeatures()
			=> EnableThreads ? "threads" : "";

		private void ParseEnumProperty<TEnum>(string name, string stringValue, out TEnum value) where TEnum : struct
		{
			if (Enum.TryParse(stringValue, true, out value))
			{
				Log.LogMessage(MessageImportance.Low, $"{name}={value}");
			}
			else
			{
				throw new NotSupportedException($"The {name} {stringValue} is not supported");
			}
		}
	}
}
