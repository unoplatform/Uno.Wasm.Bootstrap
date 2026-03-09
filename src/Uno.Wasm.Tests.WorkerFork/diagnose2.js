"use strict";
var puppeteer = require("puppeteer");

(async function () {
    var browser = await puppeteer.launch({
        headless: true,
        args: ["--no-sandbox", "--disable-setuid-sandbox"],
    });

    var page = await browser.newPage();

    var consoleLogs = [];
    var pageErrors = [];
    page.on("console", function (msg) {
        var text = msg.text();
        consoleLogs.push(text);
        // Show all non-verbose messages (skip setting env vars and diagnostic tracing MONO_WASM Loaded/Attempting)
        if (text.indexOf("Setting UNO_BOOTSTRAP") === -1 &&
            text.indexOf("Loaded:") === -1 &&
            text.indexOf("Attempting to download") === -1 &&
            text.indexOf("Throttling") === -1 &&
            text.indexOf("Resuming") === -1) {
            console.log("BROWSER: " + text);
        }
    });
    page.on("pageerror", function (err) {
        var text = err.message || err.toString();
        pageErrors.push(text);
        console.error("PAGE ERROR: " + text);
    });

    console.log("Navigating...");
    await page.goto("http://localhost:8028/", { waitUntil: "load", timeout: 120000 });

    // Wait for runtime + worker round-trip
    console.log("Waiting for #results...");
    var results = null;
    var retries = 150;
    while (results == null && retries-- > 0) {
        await new Promise(function (r) { setTimeout(r, 2000); });
        try {
            results = await page.$eval("#results", function (el) {
                return el.textContent;
            });
        } catch (_e) {
            // not present yet
        }
        // Also check module state periodically
        if (retries % 5 === 0) {
            var state = await page.evaluate(function () {
                return {
                    hasWasmModule: !!globalThis.__unoWasmModule,
                    hasModule: !!globalThis.Module,
                    hasConfig: !!globalThis.config,
                };
            });
            console.log("  State: " + JSON.stringify(state));
        }
    }

    console.log("\n=== Results ===");
    console.log("results div: " + (results || "(not found)"));

    console.log("\n=== Page Errors ===");
    pageErrors.forEach(function (e) { console.log("  " + e); });

    // Show relevant browser logs
    console.log("\n=== Key Console Logs ===");
    consoleLogs.forEach(function (l) {
        if (l.indexOf("Main thread") !== -1 ||
            l.indexOf("Worker:") !== -1 ||
            l.indexOf("Error") !== -1 ||
            l.indexOf("error") !== -1 ||
            l.indexOf("MONO") !== -1 ||
            l.indexOf("fork") !== -1) {
            console.log("  " + l);
        }
    });

    await browser.close();
    process.exit(results ? 0 : 1);
})().catch(function (err) {
    console.error("ERROR: " + err);
    process.exit(1);
});
