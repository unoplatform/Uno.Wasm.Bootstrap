"use strict";

/**
 * Loads a published build of Uno.Wasm.Tests.AOTWrapperSeeding.App in headless Chrome and checks how it ends:
 *
 *   pass   The app writes "AOTWrapperSeeding:42:42" to #results, and the runtime doesn't abort.
 *   abort  The runtime aborts with the interp.c assertion before the app writes #results. This is the build with
 *          WasmShellEnableAOTWrapperSeeding=false, and it shows that the app still reproduces the problem the seeding fixes.
 *
 * Usage:  node validate-aot-wrapper-seeding.js <pass|abort> <output-dir> [app-url]
 *
 * Exits with 0 when the app ends as expected, 1 when it doesn't (or on another error), 2 on wrong arguments and 3 when the browser fails to
 * launch.
 */

var puppeteer = require("puppeteer");
var fs = require("fs");
var path = require("path");

var expectation = process.argv[2];
var outputDir = process.argv[3] || "./artifacts";
var appUrl = process.argv[4] || "http://localhost:8000/";
var expectedResults = "AOTWrapperSeeding:42:42";

if (expectation !== "pass" && expectation !== "abort") {
    console.error("Usage: node validate-aot-wrapper-seeding.js <pass|abort> <output-dir> [app-url]");
    process.exit(2);
}

fs.mkdirSync(outputDir, { recursive: true });

(async function () {
    var browser;

    try {
        browser = await puppeteer.launch({
            headless: true,
            args: ["--no-sandbox", "--disable-setuid-sandbox"],
            defaultViewport: { width: 1280, height: 1024 },
        });
    }
    catch (e) {
        // A missing browser, or a library it needs: the app wasn't checked, which the script reports apart from an app that ends wrongly.
        console.error("ERROR: the browser failed to launch.");
        console.error(e);
        process.exit(3);
    }

    var page = await browser.newPage();
    var assertion = null;

    page.on("console", function (msg) {
        var text = msg.text();
        console.log("BROWSER: " + text);

        // The abort of interp_create_method_pointer_llvmonly when an interp_in wrapper is missing (interp.c of .NET 10), not any other assertion
        if (text.indexOf("Assertion at") >= 0 && text.indexOf("interp.c:3420") >= 0) {
            assertion = text;
        }
    });
    page.on("pageerror", function (err) {
        console.log("PAGE ERROR: " + err.message);
    });

    function readResults() {
        return page.evaluate(function () {
            var element = document.getElementById("results");
            return element ? element.textContent : null;
        });
    }

    await page.goto(appUrl);

    var results = null;

    for (var i = 0; i < 60 && results === null && assertion === null; i++) {
        await new Promise(function (resolve) { setTimeout(resolve, 1000); });
        results = await readResults();
    }

    // The assertion can arrive before the loop reads #results, even before page.goto() resolves: read it once more, after the assertion.
    results = await readResults();

    await page.screenshot({ path: path.join(outputDir, "aot-wrapper-seeding-" + expectation + ".png") });
    await browser.close();

    console.log("Results: " + results);
    console.log("Assertion: " + assertion);

    if (expectation === "pass") {
        if (assertion !== null || results !== expectedResults) {
            console.error("FAIL: expected the app to write '" + expectedResults + "' without a runtime abort.");
            process.exit(1);
        }
    }
    else if (assertion === null || results !== null) {
        console.error("FAIL: expected the runtime to abort with the interp.c assertion before the app writes its results.");
        process.exit(1);
    }

    console.log("PASS (" + expectation + ")");
})().catch(function (e) {
    console.error(e);
    process.exit(1);
});
