using System;
using System.Collections.Generic;
using System.Text;

namespace Uno.Wasm.Bootstrap
{
	internal class Constants
	{
		// NOTE: The SDK version may be overriden by an installation of the https://www.nuget.org/packages/Uno.Wasm.MonoRuntime nuget package
		public const string DefaultSdkUrl = "https://jenkins.mono-project.com/job/test-mono-mainline-wasm/3003/label=ubuntu-1804-amd64/Azure/processDownloadRequest/3003/ubuntu-1804-amd64/sdks/wasm/mono-wasm-f5cfc67c8ed.zip";
		public const string DefaultAotSDKUrl = "https://jenkins.mono-project.com/job/test-mono-mainline-wasm/3003/label=ubuntu-1804-amd64/Azure/processDownloadRequest/3003/ubuntu-1804-amd64/wasm-release-Linux-f5cfc67c8edc8a3172d146b84c6dcb3a6414c216.zip";

		/// <summary>
		/// Min version of the emscripten SDK. Must be aligned with mono's SDK build in <see cref="DefaultAotSDKUrl"/>.
		/// </summary>
		/// <remarks>
		/// The emscripten version use by mono can be found here:
		/// https://github.com/mono/mono/blob/master/sdks/builds/wasm.mk#L4
		/// </remarks>
		public static Version EmscriptenMinVersion { get; } = new Version("1.38.43");
	}
}
