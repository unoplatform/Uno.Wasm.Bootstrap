using System;
using System.Collections.Generic;
using System.Text;

namespace Uno.Wasm.Bootstrap
{
	internal class Constants
	{
		public const string DefaultDotnetRuntimeSdkUrl = "https://unowasmbootstrap.azureedge.net/runtime/"
			+ "dotnet-runtime-wasm-linux-e1fce2c-a1f0147eb6c-1877706756-Release.zip";

		/// <summary>
		/// Min version of the emscripten SDK. Must be aligned with dotnet/runtime SDK build in <see cref="NetCoreWasmSDKUri"/>.
		/// </summary>
		/// <remarks>
		/// The emscripten version use by dotnet/runtime can be found here:
		/// https://github.com/dotnet/runtime/blob/f9bb1673708ca840da0e71f9a9444ea9b0d31911/src/mono/wasm/Makefile#L32
		/// </remarks>
		public static Version DotnetRuntimeEmscriptenVersion { get; } = new Version("2.0.23");
	}
}
