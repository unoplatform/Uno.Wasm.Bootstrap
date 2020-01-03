using System;
using System.Collections.Generic;
using System.Text;

namespace Uno.Wasm.Bootstrap
{
	internal class Constants
	{
		// NOTE: The SDK version may be overriden by an installation of the https://www.nuget.org/packages/Uno.Wasm.MonoRuntime nuget package
		public const string DefaultSdkUrl = @"https://jenkins.mono-project.com/job/test-mono-mainline-wasm/label=ubuntu-1804-amd64/4515/Azure/processDownloadRequest/4515/ubuntu-1804-amd64/sdks/wasm/mono-wasm-c9a97e5258d.zip";
		public const string DefaultAotSDKUrl = "https://jenkins.mono-project.com/job/test-mono-mainline-wasm/label=ubuntu-1804-amd64/4515/Azure/processDownloadRequest/4515/ubuntu-1804-amd64/wasm-release-Linux-c9a97e5258d40d5337fe00ea38183b2f0d9fbe00.zip";

		/// <summary>
		/// Min version of the emscripten SDK. Must be aligned with mono's SDK build in <see cref="DefaultAotSDKUrl"/>.
		/// </summary>
		/// <remarks>
		/// The emscripten version use by mono can be found here:
		/// https://github.com/mono/mono/blob/master/sdks/builds/wasm.mk#L4
		/// </remarks>
		public static Version EmscriptenMinVersion { get; } = new Version("1.39.4");
	}
}
