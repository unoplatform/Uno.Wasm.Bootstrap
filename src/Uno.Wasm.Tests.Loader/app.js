// Follows the loader through a normal start and a failed one.
// usage: node app.js <base url>   (expects the RayTracer sample, which fills #results)
const puppeteer = require("puppeteer");
const http = require("http");

const baseUrl = process.argv[2] || "http://localhost:8005/";

const setMode = mode => new Promise((resolve, reject) =>
	http.get(`${baseUrl}__mode/${mode}`, res => { res.resume(); res.on("end", resolve); }).on("error", reject));

// Records the loader's phases and labels as they change
async function watchLoader(page) {
	await page.evaluateOnNewDocument(() => {
		window.__loaderLog = [];
		const record = () => {
			const loader = document.querySelector(".uno-loader");
			const entry = loader
				? `${loader.dataset.phase}|${loader.dataset.state || ""}|${loader.querySelector(".label")?.textContent || ""}|${loader.querySelector(".meta")?.textContent || ""}`
				: "removed";
			if (window.__loaderLog[window.__loaderLog.length - 1] !== entry) {
				window.__loaderLog.push(entry);
			}
		};
		new MutationObserver(record).observe(document, { subtree: true, childList: true, attributes: true, characterData: true });
	});
}

function check(condition, message) {
	console.log(`${condition ? "OK" : "FAIL"}: ${message}`);
	return condition;
}

(async () => {
	const browser = await puppeteer.launch({ headless: true, args: ["--no-sandbox", "--disable-setuid-sandbox"] });
	let ok = true;

	// Normal start
	await setMode("normal");
	let page = await browser.newPage();
	await watchLoader(page);
	await page.goto(baseUrl, { waitUntil: "domcontentloaded" });

	ok = check(await page.$("style#uno-bootstrap-css") !== null, "loader stylesheet is inlined") && ok;

	await page.waitForFunction(() => document.querySelector("#results")?.textContent, { timeout: 60000 });
	await page.waitForFunction(() => !document.querySelector(".uno-loader"), { timeout: 10000 }).catch(() => { });
	let log = await page.evaluate(() => window.__loaderLog);
	console.log(log.join("\n"));

	ok = check(log.some(e => /^download\|ok\|Downloading app\|\d+%$/.test(e)), "download phase shows a percentage") && ok;
	ok = check(log.some(e => e.startsWith("starting|")), "starting phase after the downloads") && ok;
	ok = check(log[log.length - 1] === "removed", "loader removed once the app runs") && ok;
	await page.close();

	// Failed start
	await setMode("failwasm");
	page = await browser.newPage();
	await page.goto(baseUrl, { waitUntil: "domcontentloaded" });
	const failed = await page.waitForFunction(() => document.querySelector(".uno-loader")?.dataset.state === "failed", { timeout: 90000 }).then(() => true, () => false);
	ok = check(failed, "failed state when the runtime can't load") && ok;
	ok = check(await page.$eval(".uno-loader .reload", b => getComputedStyle(b).display !== "none").catch(() => false), "reload button shown") && ok;

	await setMode("normal");
	await browser.close();
	process.exit(ok ? 0 : 1);
})();
