using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Build.Framework;
using Uno.Wasm.Bootstrap.Extensions;

namespace Uno.Wasm.Bootstrap
{
	public class UnoInstallSDKTask_v0 : Microsoft.Build.Utilities.Task
	{
		public string MonoWasmSDKUri { get; set; }

		public string MonoWasmAOTSDKUri { get; set; }

		public string MonoTempFolder { get; set; }

		[Required]
		public string PackagerOverrideFile { get; set; }

		[Required]
		public bool IsOSUnixLike { get; set; }

		[Output]
		public string SdkPath { get; set; }

		[Output]
		public string PackagerBinPath { get; set; }

		[Output]
		public string PackagerProjectFile { get; private set; }

		public override bool Execute()
		{
			InstallSdk();

			return true;
		}

		private void InstallSdk()
		{
			var sdkUri = string.IsNullOrWhiteSpace(MonoWasmSDKUri) ? Constants.DefaultSdkUrl : MonoWasmSDKUri;
			var aotUri = string.IsNullOrWhiteSpace(MonoWasmAOTSDKUri) ? Constants.DefaultAotSDKUrl : MonoWasmAOTSDKUri;

			var m = Regex.Match(sdkUri, @"(?!.*\-)(.*?)\.zip$");

			if (!m.Success)
			{
				throw new InvalidDataException($"Unable to find SHA in {sdkUri}");
			}

			var buildHash = m.Groups[1].Value;

			try
			{
				var sdkName = Path.GetFileNameWithoutExtension(new Uri(sdkUri).AbsolutePath.Replace('/', Path.DirectorySeparatorChar));

				Log.LogMessage("SDK: " + sdkName);
				SdkPath = Path.Combine(GetMonoTempPath(), sdkName);
				Log.LogMessage("SDK Path: " + SdkPath);

				if (!Directory.Exists(SdkPath))
				{
					var zipPath = SdkPath + ".zip";
					Log.LogMessage($"Using mono-wasm SDK {sdkUri}");

					RetreiveSDKFile(sdkName, sdkUri, zipPath);

					ZipFile.ExtractToDirectory(zipPath, SdkPath);
					Log.LogMessage($"Extracted {sdkName} to {SdkPath}");

					var aotZipPath = SdkPath + ".aot.zip";
					Log.LogMessage(Microsoft.Build.Framework.MessageImportance.High, $"Downloading {aotUri} to {aotZipPath}");
					RetreiveSDKFile(sdkName, aotUri, aotZipPath);

					foreach (var entry in ZipFile.OpenRead(aotZipPath).Entries)
					{
						entry.ExtractRelativeToDirectory(SdkPath, true);
					}

					Log.LogMessage($"Extracted AOT {sdkName} to {SdkPath}");

					if (IsOSUnixLike)
					{
						Process.Start("chmod", $"-R +x {SdkPath}");
					}
				}

				// Download the corresponding packager
				var packagerPath = Path.Combine(SdkPath, "packager_build");
				var packagerFilePath = Path.Combine(packagerPath, "packager.cs");

				Directory.CreateDirectory(packagerPath);

				if (!File.Exists(packagerFilePath))
				{
					File.Copy(PackagerOverrideFile, packagerFilePath, true);
				}

				PackagerBinPath = Path.Combine(SdkPath, "packager2.exe");

				var projectFile = $@"
					<Project Sdk=""Microsoft.NET.Sdk"">
					  <PropertyGroup>
						<TargetFramework>net462</TargetFramework>
						<OutputType>Exe</OutputType>
						<OutputPath>..</OutputPath>
						<AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>
					  </PropertyGroup>
						<ItemGroup>
							<Reference Include=""Mono.Cecil"">
								<HintPath>{SdkPath}/Mono.Cecil.dll</HintPath>
							</Reference>
							<Reference Include=""Mono.Options"">
								<HintPath>{SdkPath}/Mono.Options.dll</HintPath>
							</Reference>
						</ItemGroup>
					</Project>
				";

				PackagerProjectFile = Path.Combine(packagerPath, "packager2.csproj");
				File.WriteAllText(PackagerProjectFile, projectFile);

				var thisPath = Path.Combine(Path.GetDirectoryName(new Uri(GetType().Assembly.Location).LocalPath));

				File.Copy(Path.Combine(thisPath, "Mono.Cecil.dll"), Path.Combine(SdkPath, "Mono.Cecil.dll"), true);
				File.Copy(Path.Combine(thisPath, "Mono.Options.dll"), Path.Combine(SdkPath, "Mono.Options.dll"), true);
			}
			catch (Exception e)
			{
				throw new InvalidOperationException($"Failed to download the mono-wasm SDK at {sdkUri}, {e}");
			}
		}

		private void RetreiveSDKFile(string sdkName, string sdkUri, string zipPath)
		{
			var client = new WebClient();
			var wp = WebRequest.DefaultWebProxy;
			wp.Credentials = CredentialCache.DefaultCredentials;
			client.Proxy = wp;

			Log.LogMessage(Microsoft.Build.Framework.MessageImportance.High, $"Downloading {sdkName} to {zipPath}");
			client.DownloadFile(sdkUri, zipPath);
		}

		private string GetMonoTempPath()
		{
			var path = string.IsNullOrWhiteSpace(MonoTempFolder) ? Path.GetTempPath() : MonoTempFolder;

			Directory.CreateDirectory(path);

			return path;
		}
	}
}
