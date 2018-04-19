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

namespace Uno.Wasm.Bootstrap
{
	public partial class ShellTask_v0 : Microsoft.Build.Utilities.Task
	{
		private const string DefaultSdkUrl = "https://xamjenkinsartifact.azureedge.net/test-mono-mainline-webassembly/62/highsierra/sdks/wasm/mono-wasm-ddf4e7be31b.zip";

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

		[Microsoft.Build.Framework.Required]
		public string IndexHtmlPath { get; set; }

		public string MonoWasmSDKUri { get; set; }

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
			var sdkUri = string.IsNullOrWhiteSpace(MonoWasmSDKUri) ? DefaultSdkUrl : MonoWasmSDKUri;

			try
			{
				var sdkName = Path.GetFileNameWithoutExtension(new Uri(sdkUri).AbsolutePath.Replace('/', Path.DirectorySeparatorChar));
				Log.LogMessage("SDK: " + sdkName);
				_sdkPath = Path.Combine(Path.GetTempPath(), sdkName);
				Log.LogMessage("SDK Path: " + _sdkPath);

				if (Directory.Exists(_sdkPath))
				{
					return;
				}

				var client = new WebClient();
				var zipPath = _sdkPath + ".zip";
				Log.LogMessage($"Using mono-wasm SDK {sdkUri}");
				Log.LogMessage(Microsoft.Build.Framework.MessageImportance.High, $"Downloading {sdkName} to {zipPath}");
				client.DownloadFile(DefaultSdkUrl, zipPath);

				ZipFile.ExtractToDirectory(zipPath, _sdkPath);
				Log.LogMessage($"Extracted {sdkName} to {_sdkPath}");
			}
			catch(Exception e)
			{
				throw new InvalidOperationException($"Failed to download the mono-wasm SDK at {sdkUri}");
			}
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
				using (var reader = new StreamReader(IndexHtmlPath))
				{
					var html = reader.ReadToEnd();

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


					Log.LogMessage($"HTML {htmlPath}");
				}

			}
		}
	}
}
