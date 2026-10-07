namespace Uno.WebAssembly.Bootstrap {

	export type LoaderPhase = "connect" | "download" | "starting" | "failed";

	/**
	 * Drives the .uno-loader markup: the progress bar, the phase and network state text, and the exit transition.
	 * Styling lives in uno-bootstrap.css; the elements this uses are optional, so customized loaders keep working.
	 */
	export class LoaderView {
		private static readonly SLOW_AFTER_MS = 15000;
		private static readonly RETRY_VISIBLE_MS = 8000;
		private static readonly LEAVE_DURATION_MS = 300;

		private readonly _start = performance.now();
		private _phase: LoaderPhase = "connect";
		private _progress = 0;
		private _downloadedBytes = 0;
		private _retriesSeen = 0;
		private _lastRetryAt = -Infinity;
		private _timer: any;
		private _leaving = false;

		constructor(private readonly loader: HTMLElement, private readonly format: "percent" | "size") {
			this.loader.querySelector<HTMLButtonElement>(".reload")?.addEventListener("click", () => location.reload());

			if (format === "size" && typeof PerformanceObserver === "function") {
				// Transfer sizes of the files fetched so far, without needing the total
				try {
					new PerformanceObserver(list => {
						for (const entry of list.getEntries() as PerformanceResourceTiming[]) {
							this._downloadedBytes += entry.encodedBodySize || entry.transferSize || 0;
						}
					}).observe({ type: "resource", buffered: true });
				} catch {
					// Unsupported entry type: the meta line stays empty
				}
			}

			globalThis.addEventListener("online", () => this.render());
			globalThis.addEventListener("offline", () => this.render());

			// States depending on time (slow, retrying) need re-evaluating between updates
			this._timer = setInterval(() => this.render(), 500);
			this.render();
		}

		public get phase() {
			return this._phase;
		}

		public setPhase(phase: LoaderPhase) {
			if (this._phase !== "failed") {
				this._phase = phase;
				this.render();
			}
		}

		/** Progress percentage, 0 to 100. Never goes backwards: the estimated total grows as downloads are discovered. */
		public setProgress(value: number) {
			if (this._phase === "connect") {
				this._phase = "download";
			}

			this._progress = Math.max(this._progress, Math.min(value, 100));
			this.render();
		}

		/** Fades the loader out, then removes it. */
		public leave() {
			if (this._leaving) {
				return;
			}

			this._leaving = true;
			clearInterval(this._timer);

			const remove = () => this.loader.parentNode?.removeChild(this.loader);
			const reducedMotion = typeof matchMedia === "function" && matchMedia("(prefers-reduced-motion: reduce)").matches;
			if (reducedMotion) {
				remove();
				return;
			}

			this.loader.classList.add("uno-leaving");
			setTimeout(remove, LoaderView.LEAVE_DURATION_MS);
		}

		private render() {
			if (this._leaving) {
				return;
			}

			const elapsed = performance.now() - this._start;

			// Retries are counted by the resource loader, when present
			const stats = (<any>globalThis).Uno?.WebAssembly?.Bootstrap?.ResourceLoader?.stats;
			const retries = (stats?.retries ?? 0) + (stats?.stalls ?? 0);
			if (retries > this._retriesSeen) {
				this._retriesSeen = retries;
				this._lastRetryAt = elapsed;
			}

			const online = typeof navigator !== "object" || navigator.onLine !== false;
			const state =
				this._phase === "failed" ? "failed"
					: !online ? "offline"
						: this._phase === "download" && elapsed - this._lastRetryAt < LoaderView.RETRY_VISIBLE_MS ? "retry"
							: (this._phase === "download" || this._phase === "connect") && elapsed > LoaderView.SLOW_AFTER_MS ? "slow"
								: "ok";

			this.setAttribute("data-phase", this._phase);
			this.setAttribute("data-state", state);
			this.setAttribute("loading-alert", state === "failed" ? "error" : state === "ok" || state === "slow" ? "none" : "warning");

			// The fill is a CSS transform transition: the browser eases between updates on the compositor
			const shown = this._phase === "starting" ? 100 : this._progress;
			this.loader.style.setProperty("--uno-loader-progress", String(shown / 100));

			const progress = this.loader.querySelector("progress");
			if (progress) {
				// No value means indeterminate, matching the visual sweep outside the download phase
				if (this._phase === "download") {
					progress.max = 100;
					progress.value = shown;
				} else {
					progress.removeAttribute("value");
				}
			}

			let label = { connect: "Getting ready…", download: "Downloading app", starting: "Starting…", failed: "Could not load app" }[this._phase];
			let hint = "";
			let meta = "";

			if (this._phase === "download") {
				meta = this.format === "size"
					? (this._downloadedBytes > 0 ? `${(this._downloadedBytes / 1048576).toFixed(1)} MB` : "")
					: `${Math.round(this._progress)}%`;
			}

			if (state === "slow") {
				hint = "Connection looks slow. This can take a minute.";
			} else if (state === "retry") {
				label = "Connection interrupted";
				hint = "Retrying. Files already downloaded are kept.";
			} else if (state === "offline") {
				label = "Device offline";
				hint = "Loading continues once the connection is back.";
			} else if (state === "failed") {
				hint = "Reload the page to try again.";
			}

			this.setText(".label", label);
			this.setText(".meta", meta);
			this.setText(".hint", hint);
		}

		private setAttribute(name: string, value: string) {
			if (this.loader.getAttribute(name) !== value) {
				this.loader.setAttribute(name, value);
			}
		}

		private setText(selector: string, text: string) {
			const element = this.loader.querySelector(selector);
			if (element && element.textContent !== text) {
				element.textContent = text;
			}
		}
	}
}
