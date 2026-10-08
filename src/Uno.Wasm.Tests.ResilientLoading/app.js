// Loads the app through fault-server.js and waits for it to start.
// usage: node app.js <base url>   (expects the RayTracer sample, which fills #results)
const puppeteer = require("puppeteer");

const baseUrl = process.argv[2] || "http://localhost:8001/";
const timeoutMs = 120000;

async function load(browser, url, cookies = []) {
	const page = await browser.newPage();
	await page.setCookie(...cookies.map(c => ({ ...c, url })));
	page.on("console", msg => console.log(`BROWSER LOG: ${msg.text()}`));
	page.on("pageerror", err => console.log(`BROWSER ERROR: ${err}`));

	const start = Date.now();
	// A stalled resource would hold the load event back
	await page.goto(url, { waitUntil: "domcontentloaded" });

	try {
		await page.waitForFunction(() => {
			const results = document.querySelector("#results");
			return results && results.textContent;
		}, { timeout: timeoutMs, polling: 500 });
	} catch (e) {
		console.log(`FAIL: ${url} did not start within ${timeoutMs / 1000}s`);
		return false;
	}

	const results = await page.$eval("#results", e => e.textContent);
	console.log(`OK: ${url} started in ${Date.now() - start} ms (${results})`);
	await page.close();
	return true;
}

(async () => {
	const browser = await puppeteer.launch({ headless: true, args: ["--no-sandbox", "--disable-setuid-sandbox"] });

	// Every boot resource fails on its first request
	const faulted = await load(browser, baseUrl);
	const log = await new Promise((resolve, reject) => require("http").get(`${baseUrl}__log`, res => {
		let json = "";
		res.on("data", chunk => json += chunk);
		res.on("end", () => resolve(JSON.parse(json)));
	}).on("error", reject));
	console.log(`Injected ${Object.keys(log.faults).length} faults over ${log.requests} requests`);

	// A publish layout change must not silently turn this into a happy-path test
	const modes = new Set(Object.values(log.faults));
	const missing = ["reset", "stall", "503"].filter(m => !modes.has(m));
	if (missing.length) {
		console.log(`FAIL: no fault injected for: ${missing.join(", ")}`);
		await browser.close();
		process.exit(1);
	}

	// uno-bootstrap.js imported after DOMContentLoaded
	const late = await load(browser, `${baseUrl}late.html`);

	// uno-config.js cached from an earlier deployment
	const stale = await load(browser, baseUrl, [{ name: "stale", value: "1" }]);

	await browser.close();
	process.exit(faulted && late && stale ? 0 : 1);
})();
