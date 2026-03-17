"use strict";
var puppeteer = require("puppeteer");

(async function () {
    var browser = await puppeteer.launch({
        headless: true,
        args: ["--no-sandbox", "--disable-setuid-sandbox"],
    });
    var page = await browser.newPage();

    // Log ALL console messages without filtering
    page.on("console", function (msg) {
        console.log("[" + msg.type() + "] " + msg.text());
    });
    page.on("pageerror", function (err) {
        console.error("PAGE ERROR: " + (err.message || err));
    });

    await page.goto("http://localhost:8028/", { waitUntil: "load", timeout: 120000 });

    // Wait up to 30s for #results
    var results = null;
    for (var i = 0; i < 30; i++) {
        await new Promise(function (r) { setTimeout(r, 1000); });
        try {
            results = await page.$eval("#results", function (el) { return el.textContent; });
            if (results) break;
        } catch (_e) {}
    }

    console.log("\n=== RESULT: " + (results || "(not found)") + " ===");
    await browser.close();
    process.exit(results ? 0 : 1);
})().catch(function (err) {
    console.error("ERROR: " + err);
    process.exit(1);
});
