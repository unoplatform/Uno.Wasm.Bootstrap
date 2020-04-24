using System;
using System.Collections.Generic;
using System.Text;

namespace Uno.Wasm.Bootstrap
{
	class RuntimeHelpers
	{
		public static bool IsNetCore => Type.GetType("System.Runtime.Loader.AssemblyLoadContext", false) != null;

		public static bool IsMono => Type.GetType("Mono.Runtime") != null;
	}
}
