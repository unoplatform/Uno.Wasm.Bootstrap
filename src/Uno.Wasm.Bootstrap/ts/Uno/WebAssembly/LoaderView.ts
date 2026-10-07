namespace Uno.WebAssembly.Bootstrap {

	export type LoaderPhase = "connect" | "download" | "starting" | "failed";

	export type LoaderProgressFormat = "percent" | "size" | "none";

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
		private _removeTimer: any;
		private _leaving = false;
		private _observer?: PerformanceObserver;
		private readonly _onNetworkChange = () => this.render();

		/**
		 * @param statusText Names each phase and hints at a slow connection. Off by default, so a normal load shows
		 * no words to translate; retries, offline and failures are always described.
		 */
		constructor(private readonly loader: HTMLElement, private readonly format: LoaderProgressFormat, private readonly statusText = false) {
			this.loader.querySelector<HTMLButtonElement>(".reload")?.addEventListener("click", () => location.reload());

			// Normally already set in index.html; styles the compact layout used without status text
			this.setAttribute("data-status-text", statusText ? "on" : "off");
			this.setAttribute("data-progress-format", format);

			if (format === "size" && typeof PerformanceObserver === "function") {
				// Transfer sizes of the files fetched so far, without needing the total
				try {
					this._observer = new PerformanceObserver(list => {
						for (const entry of list.getEntries() as PerformanceResourceTiming[]) {
							this._downloadedBytes += entry.encodedBodySize || entry.transferSize || 0;
						}
					});
					this._observer.observe({ type: "resource", buffered: true });
				} catch {
					// Unsupported entry type: the meta line stays empty
				}
			}

			globalThis.addEventListener("online", this._onNetworkChange);
			globalThis.addEventListener("offline", this._onNetworkChange);

			// States depending on time (slow, retrying) need re-evaluating between updates
			this._timer = setInterval(() => this.render(), 500);
			this.render();
		}

		private stopListening() {
			clearInterval(this._timer);
			globalThis.removeEventListener("online", this._onNetworkChange);
			globalThis.removeEventListener("offline", this._onNetworkChange);
			this._observer?.disconnect();
		}

		public get phase() {
			return this._phase;
		}

		public setPhase(phase: LoaderPhase) {
			if (this._phase === "failed") {
				return;
			}

			this._phase = phase;

			if (phase === "failed") {
				// The app failed after the loader started leaving, or after the app removed it: bring it back
				if (this._leaving) {
					clearTimeout(this._removeTimer);
					this._leaving = false;
					this.loader.classList.remove("uno-leaving");
				}

				if (!this.loader.isConnected) {
					document.body.appendChild(this.loader);
				}
			}

			this.render();
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
			this.stopListening();

			const remove = () => this.loader.parentNode?.removeChild(this.loader);
			const reducedMotion = typeof matchMedia === "function" && matchMedia("(prefers-reduced-motion: reduce)").matches;
			if (reducedMotion) {
				remove();
				return;
			}

			this.loader.classList.add("uno-leaving");
			this._removeTimer = setTimeout(remove, LoaderView.LEAVE_DURATION_MS);
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
						: (this._phase === "download" || this._phase === "connect") && elapsed - this._lastRetryAt < LoaderView.RETRY_VISIBLE_MS ? "retry"
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

			let label = this._phase === "failed" ? "Could not load app"
				: this.statusText ? { connect: "Getting ready…", download: "Downloading app", starting: "Starting…" }[this._phase]
					: "";
			let hint = "";
			let meta = "";

			if (this._phase === "download" && this.format !== "none") {
				meta = this.format === "size"
					? (this._downloadedBytes > 0 ? `${(this._downloadedBytes / 1048576).toFixed(1)} MB` : "")
					: `${Math.round(this._progress)}%`;
			}

			if (state === "slow" && this.statusText) {
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
