using System.Runtime.CompilerServices;

namespace Mono.Profiler.Log
{
	/// <summary>
	/// Internal calls to match with https://github.com/dotnet/runtime/blob/release/6.0/src/mono/mono/profiler/log.c#L4061-L4097
	/// </summary>
	internal class LogProfiler
	{
		[MethodImplAttribute(MethodImplOptions.InternalCall)]
		public extern static void TriggerHeapshot();
	}
}
