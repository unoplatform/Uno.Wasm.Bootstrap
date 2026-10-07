// Follows the loader through a normal start, a slow one that goes offline, and a failed one.
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
			const text = selector => {
				const element = loader.querySelector(selector);
				return (element && element.textContent) || "";
			};
			const entry = loader
				? `${loader.dataset.phase}|${loader.dataset.state || ""}|${text(".label")}|${text(".meta")}`
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

	await page.waitForFunction(() => {
		const results = document.querySelector("#results");
		return results && results.textContent;
	}, { timeout: 60000 });
	await page.waitForFunction(() => !document.querySelector(".uno-loader"), { timeout: 10000 }).catch(() => { });
	let log = await page.evaluate(() => window.__loaderLog);
	console.log(log.join("\n"));

	ok = check(log.some(e => /^download\|ok\|Downloading app\|\d+%$/.test(e)), "download phase shows a percentage") && ok;
	ok = check(log.some(e => e.startsWith("starting|")), "starting phase after the downloads") && ok;
	ok = check(log[log.length - 1] === "removed", "loader removed once the app runs") && ok;
	await page.close();

	// Slow start, then offline and back
	await setMode("slow");
	page = await browser.newPage();
	await page.goto(baseUrl, { waitUntil: "domcontentloaded" });
	const loaderIs = (state, label) => page.waitForFunction((state, label) => {
		const loader = document.querySelector(".uno-loader");
		return !!loader && loader.dataset.state === state && (!label || loader.querySelector(".label").textContent === label);
	}, { timeout: 30000 }, state, label).then(() => true, () => false);
	ok = check(await loaderIs("slow"), "slow state after 15 s") && ok;
	ok = check(await page.$eval(".uno-loader .hint", h => h.textContent).catch(() => "") === "Connection looks slow. This can take a minute.", "slow hint shown") && ok;
	await page.setOfflineMode(true);
	ok = check(await loaderIs("offline", "Device offline"), "offline state when the connection drops") && ok;
	ok = check(await page.$eval(".uno-loader", l => l.getAttribute("loading-alert")).catch(() => "") === "warning", "offline shows the warning icon") && ok;
	await page.setOfflineMode(false);
	ok = check(await loaderIs("slow"), "back to slow once online") && ok;
	await page.close();

	// Failed start
	await setMode("failwasm");
	page = await browser.newPage();
	await page.goto(baseUrl, { waitUntil: "domcontentloaded" });
	const failed = await page.waitForFunction(() => {
		const loader = document.querySelector(".uno-loader");
		return !!loader && loader.dataset.state === "failed";
	}, { timeout: 90000 }).then(() => true, () => false);
	ok = check(failed, "failed state when the runtime can't load") && ok;
	ok = check(await page.$eval(".uno-loader .label", l => l.textContent).catch(() => "") === "Could not load app", "failed label") && ok;
	ok = check(await page.$eval(".uno-loader .reload", b => getComputedStyle(b).display !== "none").catch(() => false), "reload button shown") && ok;
	// Showing the Reload button must not move the logo and bar
	const barShift = await page.$eval(".uno-loader", loader => {
		const top = () => loader.querySelector(".bar").getBoundingClientRect().top;
		const withReload = top();
		loader.setAttribute("data-state", "ok");
		const withoutReload = top();
		loader.setAttribute("data-state", "failed");
		return Math.abs(withReload - withoutReload);
	}).catch(() => Infinity);
	ok = check(barShift < 1, `bar stays in place when Reload appears (moved ${barShift}px)`) && ok;

	await setMode("normal");
	await browser.close();
	process.exit(ok ? 0 : 1);
})();
