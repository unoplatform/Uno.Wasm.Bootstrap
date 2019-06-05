using System;
using System.Collections.Generic;
using System.Text;

namespace Uno.Wasm.Bootstrap
{
	internal class Constants
	{
		public const string DefaultSdkUrl = "https://jenkins.mono-project.com/job/test-mono-mainline-wasm/2972/label=ubuntu-1804-amd64/Azure/processDownloadRequest/2972/ubuntu-1804-amd64/sdks/wasm/mono-wasm-f7042d48a54.zip";
		public const string DefaultAotSDKUrl = "https://jenkins.mono-project.com/job/test-mono-mainline-wasm/2972/label=ubuntu-1804-amd64/Azure/processDownloadRequest/2972/ubuntu-1804-amd64/wasm-release-Linux-f7042d48a54fe7660a5157f52bed412030c922d5.zip";

		/// <summary>
		/// Min version of the emscripten SDK. Must be aligned with mono's SDK build in <see cref="DefaultAotSDKUrl"/>.
		/// </summary>
		/// <remarks>
		/// The emscripten version use by mono can be found here:
		/// https://github.com/mono/mono/blob/master/sdks/builds/wasm.mk#L4
		/// </remarks>
		public static Version EmscriptenMinVersion { get; } = new Version("1.38.31");
	}
}
