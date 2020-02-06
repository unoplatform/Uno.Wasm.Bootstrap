using System.Runtime.CompilerServices;

namespace WebAssembly
{
	internal sealed class Runtime
	{
		[MethodImplAttribute(MethodImplOptions.InternalCall)]
		public static extern string InvokeJS(string str, out int exceptional_result);
	}
}
