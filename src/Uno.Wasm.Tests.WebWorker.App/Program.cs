using System;
using System.Runtime.InteropServices.JavaScript;

namespace Uno.Wasm.Tests.WebWorker.App;

public static partial class Program
{
	static void Main()
	{
		Console.WriteLine("WebWorker: .NET runtime initialized successfully.");

		// Post a message back to the host page to confirm the worker is running.
		WebAssembly.Runtime.InvokeJS(
			"self.postMessage({ type: 'dotnet-ready', message: 'Hello from .NET WebWorker' })");

		Console.WriteLine("WebWorker: Posted ready message.");
	}
}
