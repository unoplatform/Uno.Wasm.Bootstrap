using System;
using System.Runtime.CompilerServices;

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
