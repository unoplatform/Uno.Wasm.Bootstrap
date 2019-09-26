using System;
using System.Collections.Generic;
using System.Text;

namespace Uno.Wasm.Bootstrap
{
	internal class Constants
	{
		// NOTE: The SDK version may be overriden by an installation of the https://www.nuget.org/packages/Uno.Wasm.MonoRuntime nuget package
		public const string DefaultSdkUrl = "https://jenkins.mono-project.com/job/test-mono-mainline-wasm/3911/label=ubuntu-1804-amd64/Azure/processDownloadRequest/3911/ubuntu-1804-amd64/sdks/wasm/mono-wasm-8da9eebcc6c.zip";
		public const string DefaultAotSDKUrl = "https://jenkins.mono-project.com/job/test-mono-mainline-wasm/3911/label=ubuntu-1804-amd64/Azure/processDownloadRequest/3911/ubuntu-1804-amd64/wasm-release-Linux-8da9eebcc6cc2f0dbdb75f88ee48ecb682b68771.zip";

		/// <summary>
		/// Min version of the emscripten SDK. Must be aligned with mono's SDK build in <see cref="DefaultAotSDKUrl"/>.
		/// </summary>
		/// <remarks>
		/// The emscripten version use by mono can be found here:
		/// https://github.com/mono/mono/blob/master/sdks/builds/wasm.mk#L4
		/// </remarks>
		public static Version EmscriptenMinVersion { get; } = new Version("1.38.45");
	}
}
