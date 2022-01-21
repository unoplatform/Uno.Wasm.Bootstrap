using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using WebAssembly;

namespace Uno
{
	public static class AotProfilerSupport
	{
		public static void Initialize()
			=> Console.WriteLine("Profiler support initialized");

		//
		// Can be called by the app to stop profilings
		//
		[MethodImpl(MethodImplOptions.NoInlining)]
		public static void StopProfile()
		{
		}

		// Called by the AOT profiler to save profile data into Module.aot_profile_data
		[MethodImpl(MethodImplOptions.NoInlining)]
		public unsafe static void DumpAotProfileData(ref byte buf, int len, string s)
		{
			fixed (void* p = &buf)
			{
				Runtime.InvokeJS($"Module.aot_profile_data = Module.HEAPU8.slice({(long)p}, {(long)p} + {len});");
			}
		}
	}
}
