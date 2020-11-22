using System;
using System.Collections.Generic;
using System.Text;

namespace Uno.Wasm.Bootstrap
{
	internal class Constants
	{
		// NOTE: The SDK version may be overriden by an installation of the https://www.nuget.org/packages/Uno.Wasm.MonoRuntime nuget package
		public const string DefaultSdkUrl = @"https://unowasmbootstrap.azureedge.net/runtime/mono-wasm-35c322c0198.zip";
		public const string DefaultAotSDKUrl = @"https://unowasmbootstrap.azureedge.net/runtime/wasm-release-Linux-35c322c0198f9ad2a0cc1c63e5612ad22af0ecd9.zip";

		public const string DefaultMonoVMSdkUrl = "https://unowasmbootstrap.azureedge.net/runtime/dotnet-runtime-wasm-dcef029-22072-Release.zip";

		/// <summary>
		/// Min version of the emscripten SDK. Must be aligned with mono's SDK build in <see cref="DefaultAotSDKUrl"/>.
		/// </summary>
		/// <remarks>
		/// The emscripten version use by mono can be found here:
		/// https://github.com/mono/mono/blob/master/sdks/builds/wasm.mk#L4
		/// </remarks>
		public static Version EmscriptenVersion { get; } = new Version("2.0.6");
	}
}
