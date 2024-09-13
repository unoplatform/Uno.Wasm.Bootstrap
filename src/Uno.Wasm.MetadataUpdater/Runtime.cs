using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Runtime.InteropServices.JavaScript;

namespace WebAssembly
{
	internal sealed class Runtime
	{
		/// <summary>
		/// Invokes Javascript code in the hosting environment
		/// </summary>
		internal static string InvokeJS(string str)
			=> Interop.InvokeJS(str);
	}

	internal static partial class Interop
	{
		[JSImport("globalThis.Uno.WebAssembly.Bootstrap.Bootstrapper.invokeJS")]
		public static partial string InvokeJS(string value);
	}
}
