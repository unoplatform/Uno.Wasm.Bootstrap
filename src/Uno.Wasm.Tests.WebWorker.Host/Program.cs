using System;

namespace Uno.Wasm.Tests.WebWorker.Host;

public static partial class Program
{
	static void Main()
	{
		Console.WriteLine("Host: Starting WebWorker host application.");

		// Create the worker and set up message handling.
		// The worker is published inside the host's hashed package folder so
		// the URL is implicitly versioned by the host's content hash:
		//   <uno_app_base>/<WasmShellWorkerBasePath>/<WasmShellWorkerFileName>
		// e.g. ./package_<hostHash>/worker/worker.js (the defaults).
		// Resolving via config.uno_app_base ensures v1 host pages always pair
		// with the v1 worker on disk (or 404), never silently with a v2 worker
		// during a rolling deployment.
		WebAssembly.Runtime.InvokeJS("""
			(function() {
				const appBase = (globalThis.config && globalThis.config.uno_app_base) || '.';
				const workerUrl = appBase + '/worker/worker.js';
				const worker = new Worker(workerUrl);
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
