namespace Uno.WebAssembly.Bootstrap {

	interface ParsedStackFrame {
		functionName: string;
		wasmFunction: number;
		offset: string;
	}

	interface AllocationSiteSnapshot {
		callSiteKey: string;
		outstandingCount: number;
		outstandingBytes: number;
		stackFrames: ParsedStackFrame[];
	}

	/**
	 * Bridge for the Emscripten built-in memory profiler.
	 *
	 * When `WasmShellEnableWasmMemoryProfiler` is enabled, this class patches
	 * Emscripten's profiler UI out, registers a Ctrl+Shift+H hotkey for export,
	 * and provides methods to download allocation snapshots in speedscope or
	 * PerfView format.
	 */
	export class EmscriptenMemoryProfilerSupport {

		/** Known allocator wrapper functions to skip when deriving call-site keys. */
		private static readonly ALLOCATOR_FUNCTIONS = new Set([
			"dlmalloc",
			"internal_memalign",
			"dlcalloc",
			"dlposix_memalign",
			"monoeg_malloc",
			"monoeg_g_calloc",
			"monoeg_malloc0",
		]);

		/**
		 * Initializes the memory profiler bridge if enabled in the configuration.
		 * Patches out Emscripten's built-in UI and registers the export hotkey.
		 */
		static initialize(unoConfig: UnoConfig): void {
			if (!unoConfig.enable_memory_profiler) {
				return;
			}

			const profiler = (<any>globalThis).emscriptenMemoryProfiler;
			if (!profiler) {
				console.warn("[MemoryProfiler] enable_memory_profiler is set but globalThis.emscriptenMemoryProfiler was not found. Ensure WasmShellEnableWasmMemoryProfiler is set to true.");
				return;
			}

			// Patch updateUi to a no-op to prevent DOM conflicts with the app's rendering
			profiler.updateUi = function () { };

			EmscriptenMemoryProfilerSupport.attachHotKey();

			console.debug("[MemoryProfiler] Emscripten memory profiler bridge activated.");
		}

		/**
		 * Returns a JSON string with summary memory counters from the profiler.
		 * Callable from the browser console.
		 */
		static getSnapshotJson(): string {
			const profiler = (<any>globalThis).emscriptenMemoryProfiler;
			if (!profiler) {
				return JSON.stringify({});
			}

			const snapshot = {
				totalMemoryAllocated: profiler.totalMemoryAllocated || 0,
				totalMemoryUsedByHeap: profiler.totalMemoryUsedByHeap || 0,
				totalTimesMallocCalled: profiler.totalTimesMallocCalled || 0,
				totalTimesFreeCalled: profiler.totalTimesFreeCalled || 0,
				totalTimesReallocCalled: profiler.totalTimesReallocCalled || 0,
				stackBase: profiler.stackBase || 0,
				stackTop: profiler.stackTop || 0,
				stackMax: profiler.stackMax || 0,
				stackTopWatermark: profiler.stackTopWatermark || 0,
				sbrkValue: profiler.sbrkValue || 0,
				allocationSiteCount: Object.keys(profiler.allocationsAtLoc || {}).length,
				totalStaticMemory: profiler.totalStaticMemory || 0,
				freeMemory: profiler.freeMemory || 0,
				pagePreRunIsFinished: profiler.pagePreRunIsFinished || false,
			};

			return JSON.stringify(snapshot);
		}

		/** Registers the Ctrl+Shift+H hotkey for snapshot export. */
		private static attachHotKey(): void {
			if (typeof document === "undefined") {
				return;
			}

			console.info(
				"[MemoryProfiler] Export hotkey: Ctrl+Shift+H (speedscope format).\n" +
				"  For PerfView format, run: Uno.WebAssembly.Bootstrap.EmscriptenMemoryProfilerSupport.downloadSnapshot(\"perfview\")"
			);

			document.addEventListener(
				"keydown",
				(evt) => {
					if (evt.ctrlKey && evt.shiftKey && evt.code === "KeyH") {
						evt.preventDefault();
						EmscriptenMemoryProfilerSupport.downloadSnapshot();
					}
				});
		}

		/**
		 * Downloads a snapshot of current native memory allocations.
		 * @param format - `"speedscope"` (default) for speedscope.app, or `"perfview"` for PerfView.
		 */
		static downloadSnapshot(format: string = "speedscope"): void {
			const profiler = (<any>globalThis).emscriptenMemoryProfiler;
			if (!profiler) {
				console.warn("[MemoryProfiler] Cannot export: profiler not available.");
				return;
			}

			const sites = EmscriptenMemoryProfilerSupport.captureAllocationSites(profiler);
			let document: any;
			let extension: string;

			if (format === "perfview") {
				document = EmscriptenMemoryProfilerSupport.buildPerfViewDocument(sites);
				extension = ".PerfView.json";
			} else {
				document = EmscriptenMemoryProfilerSupport.buildSpeedscopeDocument(sites);
				extension = ".speedscope.json";
			}

			const json = JSON.stringify(document, null, 2);
			const blob = new Blob([json], { type: "application/json" });

			const timestamp = new Date().toISOString().replace(/[:.]/g, "-");
			const filename = `memory-profile-${timestamp}${extension}`;

			const a = window.document.createElement("a");
			a.href = window.URL.createObjectURL(blob);
			a.download = filename;

			window.document.body.appendChild(a);
			a.click();
			window.document.body.removeChild(a);

			window.URL.revokeObjectURL(a.href);

			console.info(`[MemoryProfiler] Exported ${filename} (${format} format)`);
		}

		/**
		 * Builds a speedscope-compatible JSON document from the captured allocation sites.
		 * Uses a shared frame table with index references; stacks are reversed to root-to-leaf order.
		 */
		private static buildSpeedscopeDocument(sites: AllocationSiteSnapshot[]): any {
			const frameMap = new Map<string, number>();
			const frames: { name: string }[] = [];

			for (const site of sites) {
				for (const frame of site.stackFrames) {
					if (!frameMap.has(frame.functionName)) {
						frameMap.set(frame.functionName, frames.length);
						frames.push({ name: frame.functionName });
					}
				}
			}

			const samples: number[][] = [];
			const weights: number[] = [];
			let totalBytes = 0;

			for (const site of sites) {
				const indices = site.stackFrames
					.map(f => frameMap.get(f.functionName)!)
					.reverse(); // reverse to root→leaf order (speedscope expects outermost first)
				samples.push(indices);
				weights.push(site.outstandingBytes);
				totalBytes += site.outstandingBytes;
			}

			return {
				"$schema": "https://www.speedscope.app/file-format-schema.json",
				shared: {
					frames: frames,
				},
				profiles: [{
					type: "sampled",
					name: "Native Memory Allocations",
					unit: "bytes",
					startValue: 0,
					endValue: totalBytes,
					samples: samples,
					weights: weights,
				}],
			};
		}

		/**
		 * Builds a PerfView-compatible JSON document from the captured allocation sites.
		 * Stacks are kept in innermost-first order.
		 */
		private static buildPerfViewDocument(sites: AllocationSiteSnapshot[]): any {
			const perfSamples: { Stack: string[]; Metric: number }[] = [];

			for (const site of sites) {
				perfSamples.push({
					Stack: site.stackFrames.map(f => f.functionName),
					Metric: site.outstandingBytes,
				});
			}

			return {
				Samples: perfSamples,
			};
		}

		/**
		 * Iterates Emscripten's `allocationsAtLoc` and returns structured snapshots
		 * for all sites with outstanding (unfreed) allocations, sorted by bytes descending.
		 */
		private static captureAllocationSites(profiler: any): AllocationSiteSnapshot[] {
			const sites: AllocationSiteSnapshot[] = [];
			const allocationsAtLoc = profiler.allocationsAtLoc;
			if (!allocationsAtLoc) return sites;

			for (const rawStack in allocationsAtLoc) {
				if (!allocationsAtLoc.hasOwnProperty(rawStack)) continue;
				const entry = allocationsAtLoc[rawStack];
				// entry is [outstandingCount, outstandingBytes, filteredStackHtml]
				const count = entry[0];
				const bytes = entry[1];
				if (count === 0) continue;

				const frames = EmscriptenMemoryProfilerSupport.parseStackTrace(rawStack);
				sites.push({
					callSiteKey: EmscriptenMemoryProfilerSupport.deriveCallSiteKey(frames),
					outstandingCount: count,
					outstandingBytes: bytes,
					stackFrames: frames,
				});
			}

			sites.sort((a, b) => b.outstandingBytes - a.outstandingBytes);
			return sites;
		}

		/**
		 * Parses an Emscripten/V8 stack trace string into structured frames.
		 * Strips the `dotnet.native.wasm.` prefix for readability.
		 */
		private static parseStackTrace(rawStack: string): ParsedStackFrame[] {
			const lines = rawStack.split("\n");
			const frames: ParsedStackFrame[] = [];

			for (const line of lines) {
				const trimmed = line.trim();
				if (!trimmed.startsWith("at ")) continue;

				// Pattern: "at dotnet.native.wasm.funcName (url:wasm-function[N]:0xOFFSET)"
				const wasmMatch = trimmed.match(
					/^at\s+(\S+)\s+\(.*?:wasm-function\[(\d+)\]:(\S+)\)/
				);
				if (wasmMatch) {
					let funcName = wasmMatch[1];
					// Strip the dotnet.native.wasm. prefix for readability
					if (funcName.startsWith("dotnet.native.wasm.")) {
						funcName = funcName.substring("dotnet.native.wasm.".length);
					}
					frames.push({
						functionName: funcName,
						wasmFunction: parseInt(wasmMatch[2], 10),
						offset: wasmMatch[3],
					});
				} else {
					// Non-WASM frame (plain JS)
					const simpleMatch = trimmed.match(/^at\s+(\S+)/);
					let funcName = simpleMatch ? simpleMatch[1] : trimmed;
					if (funcName.startsWith("dotnet.native.wasm.")) {
						funcName = funcName.substring("dotnet.native.wasm.".length);
					}
					frames.push({
						functionName: funcName,
						wasmFunction: -1,
						offset: "",
					});
				}
			}
			return frames;
		}

		/**
		 * Returns the first non-allocator function name from the stack, identifying the true caller.
		 */
		private static deriveCallSiteKey(frames: ParsedStackFrame[]): string {
			for (const f of frames) {
				if (!EmscriptenMemoryProfilerSupport.ALLOCATOR_FUNCTIONS.has(f.functionName)) {
					return f.functionName;
				}
			}
			return frames.length > 0 ? frames[0].functionName : "unknown";
		}
	}
}
