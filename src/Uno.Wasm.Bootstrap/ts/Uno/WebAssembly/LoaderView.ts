namespace Uno.WebAssembly.Bootstrap {

	export type LoaderPhase = "connect" | "download" | "starting" | "failed";

	export interface LoaderOptions {
		/** The progress value below the bar; none by default, leaving just the logo and the bar. */
		format: LoaderProgressFormat;

		/**
		 * Names each phase and hints at a slow connection. Off by default, so a normal load shows no words to
		 * translate; retries, offline and failures are always described.
		 */
		statusText: boolean;

		/** The logo's breathing animation; on by default. */
		logoAnimation: boolean;

		/**
		 * The app replaced the loader (data-uno-loader="custom"): only publish the state, as data-phase, data-state and
		 * --uno-loader-progress on the element and as uno-loader-phase/-state/-progress events, and never touch its content.
		 */
		custom?: boolean;
	}

	/**
	 * Drives the .uno-loader markup: the progress bar, the phase and network state text, and the exit transition.
	 * Styling lives in uno-bootstrap.css; the elements this uses are optional, so customized loaders keep working.
	 */
	export class LoaderView {
		private static readonly SLOW_AFTER_MS = 15000;
		private static readonly RETRY_VISIBLE_MS = 8000;
		private static readonly LEAVE_DURATION_MS = 300;
		private static readonly CUSTOM_LEAVE_MAX_MS = 2000;

		private readonly _start = performance.now();
		private _phase: LoaderPhase = "connect";
		private _progress = 0;
		private _downloadedBytes = 0;
		private _retriesSeen = 0;
		private _lastRetryAt = -Infinity;
		private _timer: any;
		private _removeTimer: any;
		private _leaving = false;
		private _appTookOver = false;
		private _state = "";
		private _observer?: PerformanceObserver;
		private _detachObserver?: MutationObserver;
		private readonly _home: Node | null;
		private readonly _onNetworkChange = () => this.render();

		private readonly format: LoaderProgressFormat;
		private readonly statusText: boolean;
		private readonly custom: boolean;

		constructor(private readonly loader: HTMLElement, options: LoaderOptions) {
			this.format = options.format;
			this.statusText = options.statusText;
			this.custom = options.custom ?? false;
			this._home = loader.parentNode;

			if (!this.custom) {
				this.loader.querySelector<HTMLButtonElement>(".reload")?.addEventListener("click", () => location.reload());

				// Normally already set in index.html, so the first paint is right
				this.setAttribute("data-status-text", options.statusText ? "on" : "off");
				this.setAttribute("data-progress-format", options.format);
				this.setAttribute("data-logo-animation", options.logoAnimation ? "on" : "off");
			}

			this.watchDetach();

			if (options.format === "size" && typeof PerformanceObserver === "function") {
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

		/**
		 * Uno Platform removes the loader itself on the app's first frame. When that happens without leave(), put it
		 * back and fade it out over the app: mutation observers run before the next paint, so it never disappears.
		 */
		private watchDetach() {
			if (typeof MutationObserver !== "function" || !document.body) {
				return;
			}

			this._detachObserver = new MutationObserver(() => {
				if (this.loader.isConnected) {
					return;
				}

				if (this._leaving) {
					// Our own removal at the end of leave()
					this._detachObserver.disconnect();
					return;
				}

				this._appTookOver = this.loader.classList.contains("uno-keep-loader");
				const home = this._home?.isConnected ? this._home : document.body;
				home.appendChild(this.loader);
				this.leave();
			});

			this._detachObserver.observe(document.body, { childList: true, subtree: true });
		}

		public get phase() {
			return this._phase;
		}

		public setPhase(phase: LoaderPhase) {
			if (this._phase === "failed" || this._phase === phase) {
				return;
			}

			this._phase = phase;
			this.dispatch("uno-loader-phase", { phase });

			// The app was already showing its first frame (Uno Platform removed the loader): don't cover it.
			// A custom loader shows failures itself, from data-phase or the event.
			if (phase === "failed" && (this._appTookOver || this.custom)) {
				this.render();
				return;
			}

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
				this.dispatch("uno-loader-phase", { phase: this._phase });
			}

			const progress = Math.max(this._progress, Math.min(value, 100));
			if (progress !== this._progress) {
				this._progress = progress;
				this.dispatch("uno-loader-progress", { progress });
			}

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

			if (!this.custom) {
				this._removeTimer = setTimeout(remove, LoaderView.LEAVE_DURATION_MS);
				return;
			}

			// A custom loader animates .uno-leaving however it likes: remove it once that's done, or right away without one
			const style = getComputedStyle(this.loader);
			const longest = (durations: string, delays: string) => {
				const seconds = (list: string) => list.split(",").map(v => parseFloat(v) || 0);
				const d = seconds(durations), l = seconds(delays);
				return Math.max(0, ...d.map((v, i) => v + (l[i % l.length] ?? 0))) * 1000;
			};
			const duration = Math.max(
				longest(style.transitionDuration, style.transitionDelay),
				longest(style.animationDuration, style.animationDelay));

			if (duration === 0) {
				remove();
				return;
			}

			const onEnd = (e: Event) => {
				if (e.target === this.loader) {
					clearTimeout(this._removeTimer);
					remove();
				}
			};
			this.loader.addEventListener("transitionend", onEnd);
			this.loader.addEventListener("animationend", onEnd);
			this._removeTimer = setTimeout(remove, Math.min(duration + 50, LoaderView.CUSTOM_LEAVE_MAX_MS));
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
			if (state !== this._state) {
				this._state = state;
				this.dispatch("uno-loader-state", { state });
			}

			// The fill is a CSS transform transition: the browser eases between updates on the compositor
			const shown = this._phase === "starting" ? 100 : this._progress;
			this.loader.style.setProperty("--uno-loader-progress", String(shown / 100));

			if (this.custom) {
				return;
			}

			this.setAttribute("loading-alert", state === "failed" ? "error" : state === "ok" || state === "slow" ? "none" : "warning");

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

		private dispatch(type: string, detail: object) {
			if (typeof CustomEvent === "function") {
				this.loader.dispatchEvent(new CustomEvent(type, { detail, bubbles: true }));
			}
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
