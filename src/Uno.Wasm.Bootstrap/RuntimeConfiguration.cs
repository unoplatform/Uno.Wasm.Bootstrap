using System;

namespace Uno.Wasm.Bootstrap
{
	[Flags]
	public enum RuntimeConfiguration
	{
		Default = 0,
		Threads = 1 << 0,
	}
}
