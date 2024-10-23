using System;
using System.Runtime.CompilerServices;

namespace WebAssembly
{
	internal sealed class Runtime
	{
		/// <summary>
		/// Mono specific internal call.
		/// </summary>
		[MethodImpl(MethodImplOptions.InternalCall)]
		private static extern string InvokeJS(string str, out int exceptional_result);

		// Disable inlining to avoid the interpreter to evaluate an internal call that may not be available
		[MethodImpl(MethodImplOptions.NoInlining)]
		private static string MonoInvokeJS(string str, out int exceptionResult) => InvokeJS(str, out exceptionResult);

		// Disable inlining to avoid the interpreter to evaluate an internal call that may not be available
		[MethodImpl(MethodImplOptions.NoInlining)]
		private static string NetCoreInvokeJS(string str, out int exceptionResult)
			=> Interop.Runtime.InvokeJS(str, out exceptionResult);

		/// <summary>
		/// Invokes Javascript code in the hosting environment
		/// </summary>
		internal static string InvokeJS(string str)
		{
			var r = IsNetCore
			? NetCoreInvokeJS(str, out var exceptionResult)
			: MonoInvokeJS(str, out exceptionResult);

			if (exceptionResult != 0)
			{
				Console.Error.WriteLine($"Error #{exceptionResult} \"{r}\" executing javascript: \"{str}\"");
			}
			return r;
		}

		internal static bool IsNetCore { get; } = Type.GetType("System.Runtime.Loader.AssemblyLoadContext") != null;
	}
}

internal sealed class Interop
{
	internal sealed class Runtime
	{
		[MethodImplAttribute(MethodImplOptions.InternalCall)]
		public static extern string InvokeJS(string str, out int exceptional_result);
	}
}
