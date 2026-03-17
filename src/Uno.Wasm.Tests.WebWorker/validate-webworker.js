"use strict";

/**
 * Launches headless Chrome via Puppeteer, loads the published WebWorker test
 * app (either standalone or host+worker), waits for the worker to initialize
 * the .NET runtime and post messages, then verifies:
 *
 *   1. The page loads successfully.
 *   2. The worker posts a 'dotnet-ready' message (app Main ran).
 *   3. The #results element contains the expected message.
 *   4. No page errors occurred.
 *
 * Usage:  node validate-webworker.js <output-dir> [app-url]
 */

var puppeteer = require("puppeteer");
var fs = require("fs");
var path = require("path");

var outputDir = process.argv[2] || "./artifacts";
var appUrl = process.argv[3] || process.env.BOOTSTRAP_TEST_RUNNER_URL || "http://localhost:8001/";

fs.mkdirSync(outputDir, { recursive: true });

var failures = 0;

function assert(condition, message) {
    if (!condition) {
        console.error("  FAIL: " + message);
        failures++;
    } else {
        console.log("  PASS: " + message);
    }
}

(function () {
    var _run = async function () {
        console.log("Launching headless browser...");
        var browser = await puppeteer.launch({
            headless: true,
            args: ["--no-sandbox", "--disable-setuid-sandbox"],
            defaultViewport: { width: 1280, height: 1024 },
        });

        var page = await browser.newPage();

        // Collect console messages and page errors for diagnostics
        var consoleLogs = [];
        var pageErrors = [];
        page.on("console", function (msg) {
            var text = msg.text();
            consoleLogs.push(text);
            console.log("BROWSER: " + text);
        });
        page.on("pageerror", function (err) {
            var text = err.message || err.toString();
            pageErrors.push(text);
            console.error("PAGE ERROR: " + text);
        });

        // =================================================================
        // Test 1: Page loads successfully
        // =================================================================
        console.log("\n=== Test 1: Page loads ===");

        console.log("Navigating to " + appUrl);
        await page.goto(appUrl, { waitUntil: "load", timeout: 120000 });

        assert(true, "Page loaded successfully");

        // =================================================================
        // Test 2: Worker posts dotnet-ready message
        // =================================================================
        console.log("\n=== Test 2: Worker posts dotnet-ready message ===");

        var results = null;
        var retries = 60;
        while (results == null && retries-- > 0) {
            await new Promise(function (r) { setTimeout(r, 2000); });
            try {
                results = await page.$eval("#results", function (el) {
                    return el.textContent;
                });
                if (results === "") {
                    results = null; // Keep waiting
                }
            } catch (_e) {
                // element not yet present or empty
            }
        }

        await page.screenshot({
            path: path.join(outputDir, "webworker-test.png"),
        });

        assert(
            results !== null && results.length > 0,
            "Worker posted dotnet-ready message with content"
        );

        if (results) {
            console.log("  Worker message: " + results);
        }

        assert(
            results !== null && results.indexOf("Hello from .NET WebWorker") !== -1,
            "Worker message contains expected greeting"
        );

        // =================================================================
        // Test 3: Console shows expected messages
        // =================================================================
        console.log("\n=== Test 3: Console shows expected messages ===");

        var hasWorkerReady = consoleLogs.some(function (line) {
            return line.indexOf("Worker") !== -1 && line.indexOf("runtime initialized") !== -1
                || line.indexOf("Worker ready") !== -1
                || line.indexOf("dotnet-ready") !== -1
                || line.indexOf(".NET runtime initialized") !== -1;
        });

        assert(
            hasWorkerReady,
            "Console shows worker initialization messages"
        );

        // =================================================================
        // Test 4: No page errors
        // =================================================================
        console.log("\n=== Test 4: No page errors ===");

        assert(
            pageErrors.length === 0,
            "No page errors occurred" +
            (pageErrors.length > 0
                ? " (found: " + pageErrors[0].substring(0, 200) + ")"
                : "")
        );

        await browser.close();

        // =================================================================
        // Summary
        // =================================================================
        console.log("");
        if (failures > 0) {
            console.error("FAILED: " + failures + " validation(s) failed.");
            process.exit(1);
        } else {
            console.log("ALL VALIDATIONS PASSED.");
            process.exit(0);
        }
    };

    _run().catch(function (err) {
        console.error("ERROR: " + err);
        process.exit(1);
    });
})();
