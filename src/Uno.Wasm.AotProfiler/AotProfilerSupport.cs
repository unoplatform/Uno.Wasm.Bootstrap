using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using WebAssembly;

namespace Uno
{
	public static partial class AotProfilerSupport
	{
		//
		// Can be called by the app to stop profilings
		//
		[MethodImpl(MethodImplOptions.NoInlining)]
		[System.Runtime.InteropServices.JavaScript.JSExport()]
		public static void StopProfile()
			=> Console.WriteLine("Stopping AOT profile");
	}
}
