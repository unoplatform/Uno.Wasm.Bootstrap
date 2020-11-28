using System;
using System.Collections.Generic;
using System.Text;

namespace Uno.Wasm.Bootstrap
{
	internal class Constants
	{
		// NOTE: The SDK version may be overriden by an installation of the https://www.nuget.org/packages/Uno.Wasm.MonoRuntime nuget package
		public const string DefaultSdkUrl = @"https://unowasmbootstrap.azureedge.net/runtime/mono-wasm-f30f5adfbbb.zip";
		public const string DefaultAotSDKUrl = @"https://unowasmbootstrap.azureedge.net/runtime/wasm-release-Linux-f30f5adfbbb07cc4c4ba0add8ccfa35e652d3354.zip";

		// NOTE: The SDK version may be overriden by an installation of the https://www.nuget.org/packages/Uno.Wasm.MonoRuntime nuget package
		public const string DefaultDotnetRuntimeSdkUrl = "https://unowasmbootstrap.azureedge.net/runtime/dotnet-runtime-wasm-b27ff42-22309-Release.zip";

		/// <summary>
		/// Min version of the emscripten SDK. Must be aligned with mono's SDK build in <see cref="DefaultAotSDKUrl"/>.
		/// </summary>
		/// <remarks>
		/// The emscripten version use by mono can be found here:
		/// https://github.com/mono/mono/blob/master/sdks/builds/wasm.mk#L4
		/// </remarks>
		public static Version EmscriptenVersion { get; } = new Version("2.0.9");
	}
}
