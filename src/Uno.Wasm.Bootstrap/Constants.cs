using System;
using System.Collections.Generic;
using System.Text;

namespace Uno.Wasm.Bootstrap
{
	internal class Constants
	{
		public const string DefaultSdkUrl = "https://unowasmbootstrap.blob.core.windows.net/runtime/mono-wasm-f5cfc67c8ed.zip";
		public const string DefaultAotSDKUrl = "https://unowasmbootstrap.blob.core.windows.net/runtime/mono-wasm-f5cfc67c8ed.aot.zip";

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
