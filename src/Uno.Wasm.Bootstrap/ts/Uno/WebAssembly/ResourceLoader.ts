namespace Uno.WebAssembly.Bootstrap {

	/**
	 * Downloads the boot resources so that an unstable connection slows startup down instead of breaking it.
	 *
	 * The .NET loader retries a request only when it fails before the response headers arrive, and nothing has a
	 * timeout: a connection dropping mid-body fails the boot, and one that stops sending bytes hangs it forever.
	 * Every download here gets a headers timeout and an idle watchdog, and failures are retried with a jittered
	 * exponential backoff, waiting for the browser to be back online.
	 */
	export class ResourceLoader {
		public static headersTimeoutMs = 20000;
		public static idleTimeoutMs = 8000;
		public static maxAttempts = 8;

		public static readonly stats = { retries: 0, stalls: 0 };

		// Query suffix of the runtime JS modules that had to be imported from a retry URL, by file name
		private static _moduleRetrySuffixes: { [name: string]: string } = {};

		/** Reads optional overrides: UNO_BOOTSTRAP_FETCH_HEADERS_TIMEOUT_MS, _IDLE_TIMEOUT_MS and _MAX_ATTEMPTS. */
		public static configure(environmentVariables: { [key: string]: string }) {
			const read = (name: string, fallback: number) => {
				const value = parseInt(environmentVariables?.[`UNO_BOOTSTRAP_FETCH_${name}`]);
				return isNaN(value) || value <= 0 ? fallback : value;
			};

			ResourceLoader.headersTimeoutMs = read("HEADERS_TIMEOUT_MS", ResourceLoader.headersTimeoutMs);
			ResourceLoader.idleTimeoutMs = read("IDLE_TIMEOUT_MS", ResourceLoader.idleTimeoutMs);
			ResourceLoader.maxAttempts = read("MAX_ATTEMPTS", ResourceLoader.maxAttempts);
		}

		/** The .NET runtime's resource loader hook (`withResourceLoader`). */
		public static loadBootResource(type: WebAssemblyBootResourceType, name: string, defaultUri: string, integrity: string): string | Promise<Response> | undefined {
			if (type === "dotnetjs") {
				// Imported by the runtime itself, see preloadModules
				const suffix = ResourceLoader._moduleRetrySuffixes[name];
				return suffix ? defaultUri + suffix : undefined;
			}

			return type === "dotnetwasm"
				? ResourceLoader.fetchWasm(defaultUri, integrity)
				: ResourceLoader.fetchVerified(defaultUri, integrity).then(r => r.response);
		}

		/**
		 * The browser only reuses compiled WebAssembly (its code cache) for a response fetched from a URL, not one
		 * built from a buffer. Once the download succeeded, the file is in the HTTP cache: fetch it again from there.
		 */
		private static async fetchWasm(url: string, integrity: string): Promise<Response> {
			const { response, cacheable } = await ResourceLoader.fetchVerified(url, integrity);
			if (cacheable) {
				try {
					const cached = await fetch(url, { cache: "force-cache", credentials: "same-origin", integrity: integrity || undefined });
					if (cached.ok) {
						return cached;
					}
				} catch {
					// Fall back to the downloaded copy
				}
			}

			return response;
		}

		/**
		 * Imports the runtime's own JS modules before the runtime does, so its import() resolves from the module map.
		 * A failed import stays in the module map, so retries use a distinct URL, which loadBootResource then hands
		 * to the runtime. (A blob: URL would not work: emscripten resolves paths against import.meta.url.)
		 */
		public static preloadModules(urls: string[]): Promise<void> {
			return Promise.all(urls.map(async url => {
				const { retrySuffix } = await ResourceLoader.importModule(url);
				if (retrySuffix) {
					ResourceLoader._moduleRetrySuffixes[url.substring(url.lastIndexOf("/") + 1)] = retrySuffix;
				}
			})).then(() => { });
		}

		/**
		 * Imports a module, retrying with a cache-busting query. Relative URLs resolve against uno-bootstrap.js.
		 * retrySuffix is the query appended to the URL that succeeded, if any.
		 */
		public static async importModule(url: string): Promise<{ module: any, retrySuffix: string }> {
			for (let attempt = 0; ; attempt++) {
				const retrySuffix = attempt === 0 ? "" : `${url.includes("?") ? "&" : "?"}r=${attempt}`;
				let timer: any;
				try {
					// import() can't be aborted or watched for progress, so a stalled one is abandoned after a timeout
					const timeout = new Promise((_, reject) => timer = setTimeout(
						() => { ResourceLoader.stats.stalls++; reject(new Error(`Timed out importing ${url}`)); },
						ResourceLoader.headersTimeoutMs));

					//@ts-ignore
					const module = await Promise.race([import(url + retrySuffix), timeout]);
					return { module, retrySuffix };
				} catch (e) {
					await ResourceLoader.beforeRetry(attempt, url, e);
				} finally {
					clearTimeout(timer);
				}
			}
		}

		/** Loads AMD modules with require.js, retrying failed ones. */
		public static require(modules: string[], callback: Function) {
			let attempt = 0;
			const load = () => require(modules, callback, (err: RequireError) => {
				(err.requireModules ?? modules).forEach(m => requirejs.undef(m));
				ResourceLoader.beforeRetry(attempt++, modules.join(", "), err).then(load, e => console.error(e));
			});
			load();
		}

		private static async fetchVerified(url: string, integrity: string): Promise<{ response: Response, cacheable: boolean }> {
			for (let attempt = 0; ; attempt++) {
				try {
					const { body, contentType, cacheable } = await ResourceLoader.download(url, attempt === 0 ? "default" : "reload");
					await ResourceLoader.verify(body, integrity);
					return { response: new Response(body, { status: 200, headers: { "content-type": contentType } }), cacheable };
				} catch (e) {
					if (e instanceof HttpError && !e.isTransient) {
						throw e;
					}
					await ResourceLoader.beforeRetry(attempt, url, e);
				}
			}
		}

		private static async download(url: string, cache: RequestCache): Promise<{ body: Uint8Array, contentType: string, cacheable: boolean }> {
			const controller = new AbortController();
			let timer = setTimeout(() => controller.abort(), ResourceLoader.headersTimeoutMs);
			const resetIdleTimer = () => {
				clearTimeout(timer);
				timer = setTimeout(() => { ResourceLoader.stats.stalls++; controller.abort(); }, ResourceLoader.idleTimeoutMs);
			};

			try {
				// integrity is checked by verify(): with fetch({ integrity }) no bytes are exposed until the
				// whole body is in, so a stalled download could not be detected.
				const response = await fetch(url, { signal: controller.signal, cache, credentials: "same-origin" });
				if (!response.ok) {
					throw new HttpError(response.status, url);
				}

				resetIdleTimer();

				const reader = response.body.getReader();
				const chunks: Uint8Array[] = [];
				let length = 0;
				for (; ;) {
					const { done, value } = await reader.read();
					if (done) {
						break;
					}
					chunks.push(value);
					length += value.length;
					resetIdleTimer();
				}

				const body = new Uint8Array(length);
				let offset = 0;
				for (const chunk of chunks) {
					body.set(chunk, offset);
					offset += chunk.length;
				}

				return {
					body,
					contentType: response.headers.get("content-type") ?? "application/octet-stream",
					cacheable: !/no-store/i.test(response.headers.get("cache-control") ?? ""),
				};
			} finally {
				clearTimeout(timer);
			}
		}

		private static async verify(body: Uint8Array, integrity: string) {
			const [algorithm, expected] = integrity?.split("-", 2) ?? [];
			const subtleName = ({ sha256: "SHA-256", sha384: "SHA-384", sha512: "SHA-512" } as { [key: string]: string })[algorithm];

			// crypto.subtle is only available in secure contexts
			if (!subtleName || !expected || !globalThis.crypto?.subtle) {
				return;
			}

			const digest = new Uint8Array(await crypto.subtle.digest(subtleName, body));
			let binary = "";
			for (let i = 0; i < digest.length; i++) {
				binary += String.fromCharCode(digest[i]);
			}

			if (btoa(binary) !== expected) {
				throw new Error(`Integrity check failed for downloaded content`);
			}
		}

		private static async beforeRetry(attempt: number, what: string, error: any) {
			if (attempt + 1 >= ResourceLoader.maxAttempts) {
				throw error;
			}

			ResourceLoader.stats.retries++;
			console.debug(`[Bootstrap] Retrying ${what} (attempt ${attempt + 2}/${ResourceLoader.maxAttempts}): ${error}`);

			const delay = Math.min(4000, 250 * 2 ** attempt) * (0.5 + Math.random());
			await new Promise(resolve => setTimeout(resolve, delay));

			if (typeof navigator === "object" && navigator.onLine === false) {
				await new Promise(resolve => globalThis.addEventListener("online", resolve, { once: true }));
			}
		}
	}

	class HttpError extends Error {
		constructor(public readonly status: number, url: string) {
			super(`HTTP ${status} for ${url}`);
		}

		public get isTransient() {
			return this.status >= 500 || this.status === 408 || this.status === 429;
		}
	}
}
