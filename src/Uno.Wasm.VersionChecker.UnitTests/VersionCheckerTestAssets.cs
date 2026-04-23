using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Versioning;
using Uno.VersionChecker;
using Uno.Wasm.WebCIL;

namespace Uno.Wasm.VersionChecker.UnitTests;

internal static class VersionCheckerTestAssets
{
	private static readonly Lazy<byte[]> MainAssemblyBytesValue = new(() => File.ReadAllBytes(typeof(VersionCheckerReplHost).Assembly.Location));
	private static readonly Lazy<byte[]> RuntimeAssemblyBytesValue = new(() => File.ReadAllBytes(typeof(object).Assembly.Location));
	private static readonly Lazy<byte[]> WebcilAssemblyBytesValue = new(CreateWebcilAssemblyBytes);

	public static byte[] MainAssemblyBytes => MainAssemblyBytesValue.Value;
	public static byte[] RuntimeAssemblyBytes => RuntimeAssemblyBytesValue.Value;
	public static byte[] WebcilAssemblyBytes => WebcilAssemblyBytesValue.Value;

	public static string MainAssemblyName => typeof(VersionCheckerReplHost).Assembly.GetName().Name!;
	public static string MainAssemblyVersion => GetInformationalVersion(typeof(VersionCheckerReplHost).Assembly);
	public static string MainAssemblyFramework => typeof(VersionCheckerReplHost).Assembly.GetCustomAttribute<TargetFrameworkAttribute>()!.FrameworkName!;
	public static string MainAssemblyConfiguration => typeof(VersionCheckerReplHost).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration ?? string.Empty;
	public static string RuntimeAssemblyName => typeof(object).Assembly.GetName().Name!;
	public static string RuntimeAssemblyVersion => GetInformationalVersion(typeof(object).Assembly);
	public static string RuntimeAssemblyFramework => typeof(object).Assembly.GetCustomAttribute<TargetFrameworkAttribute>()!.FrameworkName!;

	private static byte[] CreateWebcilAssemblyBytes()
	{
		var sourceAssembly = Path.Combine(
			AppContext.BaseDirectory,
			"..",
			"..",
			"..",
			"..",
			"Uno.Wasm.MixedModeRoslynSample",
			"sdk",
			"System.ValueTuple.dll");
		sourceAssembly = Path.GetFullPath(sourceAssembly);

		var tempFile = Path.Combine(Path.GetTempPath(), $"version-checker-{Guid.NewGuid():N}.wasm");
		try
		{
			WebcilConverterUtil.ConvertToWebcil(sourceAssembly, tempFile);
			return File.ReadAllBytes(tempFile);
		}
		finally
		{
			if (File.Exists(tempFile))
			{
				File.Delete(tempFile);
			}
		}
	}

	private static string GetInformationalVersion(Assembly assembly) =>
		assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion?.Split('+').FirstOrDefault()
		?? assembly.GetName().Version?.ToString()
		?? FileVersionInfo.GetVersionInfo(assembly.Location).ProductVersion
		?? string.Empty;
}
