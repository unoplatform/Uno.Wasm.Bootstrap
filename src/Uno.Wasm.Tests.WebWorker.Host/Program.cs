using System;
using System.Runtime.InteropServices.JavaScript;

namespace Uno.Wasm.Tests.WebWorker.Host;

public static partial class Program
{
	static void Main()
	{
		Console.WriteLine("Host: Starting WebWorker host application.");

		// Create the worker and set up message handling.
		// The worker files are published under _worker/ by the build system.
		WebAssembly.Runtime.InvokeJS("""
			(function() {
				const worker = new Worker('./_worker/worker.js');
				const resultsEl = document.getElementById('results')
					|| (function() {
						var el = document.createElement('div');
						el.id = 'results';
						document.body.appendChild(el);
						return el;
					})();

				worker.addEventListener('message', function(e) {
					if (e.data && e.data.type === 'dotnet-ready') {
						resultsEl.textContent = e.data.message;
						console.log('Host: Received worker message: ' + e.data.message);
					} else if (e.data && e.data.type === 'uno-worker-ready') {
						console.log('Host: Worker runtime initialized.');
					}
				});

				worker.addEventListener('error', function(e) {
					console.error('Host: Worker error: ' + e.message);
					resultsEl.textContent = 'Worker error: ' + e.message;
				});
			})()
			""");

		Console.WriteLine("Host: Worker created, waiting for messages.");
	}
}
