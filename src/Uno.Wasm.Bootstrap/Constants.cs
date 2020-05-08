using System;
using System.Collections.Generic;
using System.Text;

namespace Uno.Wasm.Bootstrap
{
	internal class Constants
	{
		// NOTE: The SDK version may be overriden by an installation of the https://www.nuget.org/packages/Uno.Wasm.MonoRuntime nuget package
		public const string DefaultSdkUrl = @"https://unowasmbootstrap.blob.core.windows.net/runtime/mono-wasm-502dca36d36.zip";
		public const string DefaultAotSDKUrl = @"https://unowasmbootstrap.blob.core.windows.net/runtime/wasm-release-Linux-502dca36d361ce0ebc9e9c3a2f1d43ff8b953be7.zip";

		/// <summary>
		/// Min version of the emscripten SDK. Must be aligned with mono's SDK build in <see cref="DefaultAotSDKUrl"/>.
		/// </summary>
		/// <remarks>
		/// The emscripten version use by mono can be found here:
		/// https://github.com/mono/mono/blob/master/sdks/builds/wasm.mk#L4
		/// </remarks>
		public static Version EmscriptenMinVersion { get; } = new Version("1.39.11");
	}
}
