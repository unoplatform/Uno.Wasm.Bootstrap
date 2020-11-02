using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Build.Framework;
using Uno.Wasm.Bootstrap.Extensions;

namespace Uno.Wasm.Bootstrap
{
	public class UnoInstallSDKTask_v0 : Microsoft.Build.Utilities.Task
	{
		public string? MonoWasmSDKUri { get; set; }

		public string? MonoWasmAOTSDKUri { get; set; }

		public string? MonoTempFolder { get; set; }

		[Required]
		public string PackagerOverrideFolderPath { get; set; } = "";

		[Required]
		public bool IsOSUnixLike { get; set; }

		[Microsoft.Build.Framework.Required]
		public string MonoRuntimeExecutionMode { get; set; } = "";

		[Microsoft.Build.Framework.Required]
		public Microsoft.Build.Framework.ITaskItem[]? Assets { get; set; }

		public bool GenerateAOTProfile { get; set; } = false;

		[Output]
		public string? SdkPath { get; set; }

		[Output]
		public string? PackagerBinPath { get; set; }

		[Output]
		public string? PackagerProjectFile { get; private set; }

		public override bool Execute()
		{
			InstallSdk();

			return true;
		}

		private void InstallSdk()
		{
			var runtimeExecutionMode = ParseRuntimeExecutionMode();

			var sdkUri = string.IsNullOrWhiteSpace(MonoWasmSDKUri) ? Constants.DefaultSdkUrl : MonoWasmSDKUri!;
			var aotUri = string.IsNullOrWhiteSpace(MonoWasmAOTSDKUri) ? Constants.DefaultAotSDKUrl : MonoWasmAOTSDKUri!;

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

				var writeChecksum = false;

				if (Directory.Exists(SdkPath) && !VerifyChecksum(SdkPath))
				{
					// SDK folder was tampered with (e.g. StorageSense, User, etc.)
					Log.LogMessage($"Removing invalid mono-wasm SDK: {SdkPath}");

					var destination = $"{SdkPath}.{Guid.NewGuid():N}";

					Directory.Move(SdkPath, destination);

					Directory.Delete(destination, true);
				}

				if (!Directory.Exists(SdkPath))
				{
					var zipPath = SdkPath + ".zip";
					Log.LogMessage($"Using mono-wasm SDK {sdkUri}");

					zipPath = RetreiveSDKFile(sdkName, sdkUri, zipPath);

					ZipFile.ExtractToDirectory(zipPath, SdkPath);
					Log.LogMessage($"Extracted {sdkName} to {SdkPath}");

					writeChecksum = true;
				}

				if (
					(
					runtimeExecutionMode == RuntimeExecutionMode.FullAOT
					|| runtimeExecutionMode == RuntimeExecutionMode.InterpreterAndAOT
					|| HasBitcodeAssets()
					|| GenerateAOTProfile
					)
					&& !Directory.Exists(Path.Combine(SdkPath, "wasm-cross-release"))
				)
				{
					var aotZipPath = SdkPath + ".aot.zip";
					Log.LogMessage(Microsoft.Build.Framework.MessageImportance.High, $"Downloading {aotUri} to {aotZipPath}");
					aotZipPath = RetreiveSDKFile(sdkName, aotUri, aotZipPath);

					foreach (var entry in ZipFile.OpenRead(aotZipPath).Entries)
					{
						entry.ExtractRelativeToDirectory(SdkPath, true);
					}

					Log.LogMessage($"Extracted AOT {sdkName} to {SdkPath}");

					if (IsOSUnixLike)
					{
						Process.Start("chmod", $"-R +x {SdkPath}");
					}

					writeChecksum = true;
				}

				if (!string.IsNullOrEmpty(PackagerOverrideFolderPath))
				{
					PackagerBinPath = Path.Combine(SdkPath, "packager2.exe");

					foreach (var file in Directory.EnumerateFiles(PackagerOverrideFolderPath))
					{
						var destFileName = Path.Combine(SdkPath, Path.GetFileName(file));
						Log.LogMessage($"Copy packager override {file} to {destFileName}");
						File.Copy(file, destFileName, true);
					}
				}

				if (writeChecksum)
				{
					WriteChecksum(SdkPath);
					Log.LogMessage($"Wrote checksum to {SdkPath}");
				}
			}
			catch (Exception e)
			{
				throw new InvalidOperationException($"Failed to download the mono-wasm SDK at {sdkUri}, {e}");
			}
		}

		static readonly string[] BitCodeExtensions = new string[] { ".bc", ".a" };

		private bool HasBitcodeAssets()
			=> Assets.Any(asset => BitCodeExtensions.Any(ext => asset.ItemSpec.EndsWith(ext, StringComparison.OrdinalIgnoreCase)));

		private string RetreiveSDKFile(string sdkName, string sdkUri, string zipPath)
		{
			var tries = 3;

			while (--tries > 0)
			{
				try
				{
					var uri = new Uri(sdkUri);

					if (!uri.IsFile)
					{
						var client = new WebClient();
						var wp = WebRequest.DefaultWebProxy;
						wp.Credentials = CredentialCache.DefaultCredentials;
						client.Proxy = wp;

						Log.LogMessage(Microsoft.Build.Framework.MessageImportance.High, $"Downloading {sdkName} to {zipPath}");
						client.DownloadFile(sdkUri, zipPath);

						return zipPath;
					}
					else
					{
						return uri.LocalPath;
					}
				}
				catch(Exception e)
				{
					Log.LogWarning($"Failed to download Downloading {sdkName} to {zipPath}. Retrying... ({e.Message})");
				}
			}

			throw new Exception($"Failed to download {sdkName} to {zipPath}");
		}

		private const string ChecksumFilename = "UNO_WASM_SDK.CHECKSUM";

		private int ComputeChecksum(string path)
		{
			var exclusions = new[]
			{
				Path.Combine(path, ChecksumFilename),
				Path.Combine(path, Path.Combine("wasm-bcl", "wasm_tools", "monolinker.exe.config"))
			};

			return Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
				.Where(f => !exclusions.Contains(f))
				.Sum(f => f.Length);
		}

		private bool VerifyChecksum(string path)
		{
			try
			{
				var checksum = int.Parse(File.ReadAllText(Path.Combine(path, ChecksumFilename)));

				return ComputeChecksum(path) == checksum;
			}
			catch
			{
				return false;
			}
		}

		private void WriteChecksum(string path)
		{
			var file = Path.Combine(path, ChecksumFilename);

			File.WriteAllText(file, $"{ComputeChecksum(path)}");
		}

		private string GetMonoTempPath()
		{
			var path = string.IsNullOrWhiteSpace(MonoTempFolder) ? Path.GetTempPath() : MonoTempFolder!;

			Directory.CreateDirectory(path);

			return path;
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
	}
}
