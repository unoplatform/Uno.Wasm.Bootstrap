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
        consoleLogs.push(msg.text());
        console.log("BROWSER: " + msg.text());
    });
    page.on("pageerror", function (err) {
        pageErrors.push(err.message || err.toString());
        console.error("PAGE ERROR: " + (err.message || err.toString()));
    });

    console.log("Navigating...");
    await page.goto("http://localhost:8001/", { waitUntil: "load", timeout: 120000 });

    // Wait for runtime to initialize
    console.log("Waiting for runtime...");
    var retries = 30;
    var moduleInfo = null;
    while (moduleInfo == null && retries-- > 0) {
        await new Promise(function (r) { setTimeout(r, 2000); });
        moduleInfo = await page.evaluate(function () {
            try {
                var mod = globalThis.Module;
                if (!mod) return null;
                return {
                    hasModule: !!mod,
                    hasWasmModule: !!mod.wasmModule,
                    wasmModuleType: typeof mod.wasmModule,
                    wasmModuleConstructor: mod.wasmModule ? mod.wasmModule.constructor.name : "N/A",
                    hasConfig: !!globalThis.config,
                    configKeys: globalThis.config ? Object.keys(globalThis.config).join(",") : "N/A",
                    unoMain: globalThis.config ? globalThis.config.uno_main : "N/A",
                    dotnetJsFilename: globalThis.config ? globalThis.config.dotnet_js_filename : "N/A",
                    unoAppBase: globalThis.config ? globalThis.config.uno_app_base : "N/A",
                    moduleKeys: Object.keys(mod).filter(function(k) { return k.indexOf("wasm") !== -1 || k === "FS" || k === "HEAP8"; }).join(","),
                };
            } catch (e) {
                return { error: e.toString() };
            }
        });
    }

    console.log("\n=== Module Info ===");
    console.log(JSON.stringify(moduleInfo, null, 2));

    console.log("\n=== Console Logs ===");
    consoleLogs.forEach(function(l) { console.log("  " + l); });

    console.log("\n=== Page Errors ===");
    pageErrors.forEach(function(e) { console.log("  " + e); });

    await browser.close();
})().catch(function (err) {
    console.error("ERROR: " + err);
    process.exit(1);
});
