// Loads the app once to install its service worker, then reloads it offline and on a flaky network.
// usage: node app.js <base url>   (expects the RayTracer sample, which fills #results)
const puppeteer = require("puppeteer");
const http = require("http");

const baseUrl = process.argv[2] || "http://localhost:8002/";
const timeoutMs = 60000;

const status = urlPath => new Promise((resolve, reject) =>
	http.get(new URL(urlPath, baseUrl), res => { res.resume(); resolve(res.statusCode); }).on("error", reject));

const setMode = mode => new Promise((resolve, reject) =>
	http.get(`${baseUrl}__mode/${mode}`, res => { res.resume(); res.on("end", resolve); }).on("error", reject));

async function waitForStart(page, label) {
	const start = Date.now();
	try {
		await page.waitForFunction(() => document.querySelector("#results")?.textContent, { timeout: timeoutMs, polling: 250 });
		console.log(`OK: ${label} started in ${Date.now() - start} ms`);
		return true;
	} catch (e) {
		console.log(`FAIL: ${label} did not start within ${timeoutMs / 1000}s`);
		return false;
	}
}

(async () => {
	const browser = await puppeteer.launch({ headless: true, args: ["--no-sandbox", "--disable-setuid-sandbox"] });
	const page = await browser.newPage();
	page.on("console", msg => console.log(`BROWSER LOG: ${msg.text()}`));
	page.on("pageerror", err => console.log(`BROWSER ERROR: ${err}`));

	await setMode("normal");
	await page.goto(baseUrl, { waitUntil: "domcontentloaded" });
	let ok = await waitForStart(page, "first visit");

	// The worker registers after the app is up, then precaches; wait for it to control the page and settle
	ok = ok && await page.evaluate(async () => {
		const end = Date.now() + 90000;
		while (!navigator.serviceWorker.controller && Date.now() < end) {
			await new Promise(r => setTimeout(r, 250));
		}

		// The background precache is requested a few seconds after the app started
		await new Promise(r => setTimeout(r, 5000));

		let last = -1, stable = 0;
		while (stable < 8 && Date.now() < end) {
			let count = 0;
			for (const name of await caches.keys()) {
				count += (await (await caches.open(name)).keys()).length;
			}
			stable = count === last ? stable + 1 : 0;
			last = count;
			await new Promise(r => setTimeout(r, 500));
		}
		return !!navigator.serviceWorker.controller && last > 0;
	});
	console.log(ok ? "OK: service worker active and caches settled" : "FAIL: service worker did not take control");

	// The sample excludes pwa-images/** from precaching (WasmShellPWAPrecacheExclude); the browser may still
	// fetch, and so cache, a manifest icon or two.
	if (ok) {
		const counts = await page.evaluate(async () => {
			const cached = [];
			for (const name of await caches.keys()) {
				cached.push(...(await (await caches.open(name)).keys()).map(r => new URL(r.url).pathname));
			}
			const config = await import(document.querySelector('script[type="module"][src*="uno-bootstrap.js"]').src.replace(/uno-bootstrap\.js.*$/, "uno-config.js"));
			// The WebWorker's _framework entries keep their unexpanded "#[.{fingerprint}]" placeholder, so they 404
			const offline = config.config.offline_files.filter(f => !f.includes("#[")).map(f => new URL(f, location.href).pathname);
			const isImage = p => p.includes("/pwa-images/");
			return {
				listedImages: offline.filter(isImage).length,
				cachedImages: cached.filter(isImage).length,
				otherMissing: offline.filter(p => !isImage(p) && !p.endsWith("/.") && !cached.includes(p)),
			};
		});
		// Entries the server doesn't have can't be cached (stale WebWorker entries in offline_files)
		const missing = [];
		for (const p of [...new Set(counts.otherMissing)]) {
			if (await status(p) !== 404) {
				missing.push(p);
			}
		}

		console.log(`Precache: ${counts.cachedImages}/${counts.listedImages} excluded images cached, ${missing.length} other offline files missing ${missing.slice(0, 5).join(", ")}`);
		ok = counts.listedImages > 0 && counts.cachedImages <= 2 && missing.length === 0;
		console.log(ok ? "OK: precache honors WasmShellPWAPrecacheExclude" : "FAIL: unexpected precache contents");
	}

	for (const mode of ["offline", "flaky"]) {
		if (!ok) {
			break;
		}
		await setMode(mode);
		await page.goto(baseUrl, { waitUntil: "domcontentloaded" }).catch(e => console.log(`navigation: ${e.message}`));
		ok = await waitForStart(page, `${mode} reload`);
	}

	await setMode("normal");
	await browser.close();
	process.exit(ok ? 0 : 1);
})();
