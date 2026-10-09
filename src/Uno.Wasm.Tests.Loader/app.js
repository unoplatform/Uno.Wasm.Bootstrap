// Follows the loader through a normal start, a slow one that goes offline, a failed one, Uno Platform's hand-off and a custom loader.
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

	// The AppManifest colors are in index.html, so they're right before the bootstrapper's script runs
	ok = check(await page.$eval(".uno-loader", l => l.dataset.manifest).catch(() => null) === "baked", "manifest is baked into index.html") && ok;
	ok = check(await page.$eval(".uno-loader", l => getComputedStyle(l).backgroundColor).catch(() => "") === "rgb(253, 246, 227)", "light theme background from the manifest on the first paint") && ok;

	// By default it's just the logo and the bar: the text block below takes no space
	await page.waitForFunction(() => document.querySelector(".uno-loader")?.dataset.phase === "download", { timeout: 30000 }).catch(() => { });
	// Layout offsets, not client rects: the text block's fade-in animates a transform
	const below = await page.$eval(".uno-loader", loader => {
		const bar = loader.querySelector(".bar"), info = loader.querySelector(".info");
		return info.offsetTop + info.offsetHeight - (bar.offsetTop + bar.offsetHeight);
	}).catch(() => Infinity);
	ok = check(below === 0, `nothing takes space below the bar (${below}px)`) && ok;

	// Where ExtendedSplashScreen draws it, so the hand-off doesn't jump
	const offCentre = await page.$eval(".uno-loader .logo", logo => {
		const r = logo.getBoundingClientRect();
		return Math.max(Math.abs(r.left + r.width / 2 - innerWidth / 2), Math.abs(r.top + r.height / 2 - innerHeight / 2));
	}).catch(() => Infinity);
	ok = check(offCentre < 1, `logo is centred in the viewport (off by ${offCentre.toFixed(1)}px)`) && ok;
	ok = check(await page.$eval(".uno-loader .logo", l => getComputedStyle(l).animationName.includes("uno-loader-breathe")).catch(() => false), "logo breathes by default") && ok;

	await page.waitForFunction(() => {
		const results = document.querySelector("#results");
		return results && results.textContent;
	}, { timeout: 60000 });
	await page.waitForFunction(() => !document.querySelector(".uno-loader"), { timeout: 10000 }).catch(() => { });
	let log = await page.evaluate(() => window.__loaderLog);
	console.log(log.join("\n"));

	ok = check(log.some(e => e === "download|ok||"), "download phase shows no text") && ok;
	ok = check(!log.some(e => /\|(Getting ready…|Downloading app|Starting…)\|/.test(e)), "no phase labels by default") && ok;
	ok = check(log.some(e => e.startsWith("starting|")), "starting phase after the downloads") && ok;
	ok = check(log[log.length - 1] === "removed", "loader removed once the app runs") && ok;
	ok = check(await page.evaluate(() => getComputedStyle(document.documentElement).backgroundColor) === "rgba(0, 0, 0, 0)", "the page background is left to the app once the loader is gone") && ok;
	await page.close();

	// A policy sent by the server blocks the inlined stylesheet and the baked style attributes: the bootstrapper loads
	// the stylesheet as a file and applies the manifest itself
	await setMode("csp");
	page = await browser.newPage();
	await page.goto(baseUrl, { waitUntil: "domcontentloaded" });
	await page.waitForFunction(() => getComputedStyle(document.querySelector(".uno-loader")).position === "fixed", { timeout: 30000 }).catch(() => { });
	ok = check(await page.$eval(".uno-loader", l => getComputedStyle(l).position).catch(() => "") === "fixed", "loader styled under a server-sent Content-Security-Policy") && ok;
	await page.waitForFunction(() => getComputedStyle(document.querySelector(".uno-loader")).backgroundColor === "rgb(253, 246, 227)", { timeout: 30000 }).catch(() => { });
	ok = check(await page.$eval(".uno-loader", l => getComputedStyle(l).backgroundColor).catch(() => "") === "rgb(253, 246, 227)", "and gets the manifest colors") && ok;
	await page.close();
	await setMode("normal");

	// Dark theme background from the manifest
	page = await browser.newPage();
	await page.emulateMediaFeatures([{ name: "prefers-color-scheme", value: "dark" }]);
	await page.goto(baseUrl, { waitUntil: "domcontentloaded" });
	ok = check(await page.$eval(".uno-loader", l => getComputedStyle(l).backgroundColor).catch(() => "") === "rgb(0, 43, 54)", "dark theme background from the manifest") && ok;
	await page.close();

	// Uno Platform's hand-off: it keeps the loader up, then removes the element itself on its first frame. The
	// RayTracer sample replaces the page content once it runs, which removes the loader the same way.
	await setMode("keep");
	page = await browser.newPage();
	await page.evaluateOnNewDocument(() => {
		window.__handOff = {};
		let loader = null;
		new MutationObserver(() => {
			loader ??= document.querySelector(".uno-loader");
			if (!loader) {
				return;
			}
			const h = window.__handOff;
			if (!loader.isConnected && !h.removedAt) {
				h.removedAt = performance.now();
				h.kept = loader.classList.contains("uno-keep-loader");
			} else if (loader.isConnected && h.removedAt && !h.backAt) {
				h.backAt = performance.now();
				h.fading = loader.classList.contains("uno-leaving");
			} else if (!loader.isConnected && h.backAt && !h.goneAt) {
				h.goneAt = performance.now();
			}
		}).observe(document, { subtree: true, childList: true });
	});
	await page.goto(baseUrl, { waitUntil: "domcontentloaded" });
	await page.waitForFunction(() => window.__handOff.goneAt, { timeout: 60000 }).catch(() => { });
	const handOff = await page.evaluate(() => window.__handOff);
	ok = check(handOff.kept === true, "loader kept until the app removes it") && ok;
	ok = check(handOff.fading === true && handOff.backAt - handOff.removedAt < 50, "then it's put back and fades out") && ok;
	const fadeMs = Math.round((handOff.goneAt ?? 0) - (handOff.backAt ?? 0));
	ok = check(handOff.goneAt > 0 && fadeMs >= 200, `and is removed after the fade (${fadeMs} ms)`) && ok;
	await page.close();

	// An app-provided loader: the bootstrapper publishes the state and leaves the content alone
	await setMode("custom");
	page = await browser.newPage();
	await page.evaluateOnNewDocument(() => {
		window.__phases = [];
		document.addEventListener("uno-loader-phase", e => window.__phases.push(e.detail.phase));
		new MutationObserver(() => {
			const loader = document.querySelector(".uno-loader");
			if (loader?.classList.contains("uno-leaving") && !window.__leavingAt) {
				window.__leavingAt = performance.now();
			}
			if (!loader && window.__leavingAt && !window.__removedAt) {
				window.__removedAt = performance.now();
			}
		}).observe(document, { subtree: true, childList: true, attributes: true });
	});
	await page.goto(baseUrl, { waitUntil: "domcontentloaded" });
	await page.waitForFunction(() => document.querySelector(".uno-loader")?.dataset.phase === "download", { timeout: 30000 }).catch(() => { });
	const custom = await page.$eval(".uno-loader", l => ({
		text: l.querySelector(".mine").textContent,
		progressValue: l.querySelector("progress").hasAttribute("value"),
		alert: l.hasAttribute("loading-alert"),
		statusText: l.hasAttribute("data-status-text"),
	})).catch(() => null);
	ok = check(custom && custom.text === "Custom" && !custom.progressValue && !custom.alert && !custom.statusText, "custom loader content and attributes left alone") && ok;
	await page.waitForFunction(() => window.__removedAt, { timeout: 60000 }).catch(() => { });
	const customExit = await page.evaluate(() => window.__leavingAt && window.__removedAt ? Math.round(window.__removedAt - window.__leavingAt) : -1);
	const phases = await page.evaluate(() => window.__phases);
	ok = check(phases.includes("download") && phases.includes("starting"), `phase events (${phases.join(", ")})`) && ok;
	ok = check(customExit >= 300, `custom loader's own exit transition runs before removal (${customExit} ms)`) && ok;
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
	ok = check(await page.$eval(".uno-loader .hint", h => h.textContent).catch(() => null) === "", "no slow hint by default") && ok;
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
	ok = check(await page.$eval(".uno-loader", l => getComputedStyle(l.querySelector(".status")).color === getComputedStyle(l.querySelector(".meta")).color).catch(() => false), "failure label in the same grey as the progress value") && ok;
	// The failure text takes the place of the hidden bar, and the logo doesn't move
	const failedLayout = await page.$eval(".uno-loader", loader => {
		const top = s => loader.querySelector(s).getBoundingClientRect().top;
		const texts = [".label", ".hint"].map(s => loader.querySelector(s));
		const saved = texts.map(e => e.textContent);
		const logoWithFailure = top(".logo");
		const statusWithFailure = top(".status");
		const barShown = getComputedStyle(loader.querySelector(".bar")).display !== "none";
		loader.setAttribute("data-state", "ok");
		loader.setAttribute("loading-alert", "none");
		texts.forEach(e => e.textContent = "");
		const logoWithoutFailure = top(".logo");
		const barWithoutFailure = top(".bar");
		texts.forEach((e, i) => e.textContent = saved[i]);
		loader.setAttribute("loading-alert", "error");
		loader.setAttribute("data-state", "failed");
		return { barShown, logoShift: Math.abs(logoWithFailure - logoWithoutFailure), textOffset: Math.abs(statusWithFailure - barWithoutFailure) };
	}).catch(() => ({ barShown: true, logoShift: Infinity, textOffset: Infinity }));
	ok = check(!failedLayout.barShown, "bar removed when the failure is shown") && ok;
	ok = check(failedLayout.textOffset < 1, `failure text starts where the bar was (off by ${failedLayout.textOffset}px)`) && ok;
	ok = check(failedLayout.logoShift < 1, `logo stays in place when the failure is shown (moved ${failedLayout.logoShift}px)`) && ok;

	await setMode("normal");
	await browser.close();
	process.exit(ok ? 0 : 1);
})();
