"use strict";

/**
 * Launches headless Chrome via Puppeteer, loads the published WorkerFork test
 * app, waits for the app to fork a WebWorker, exchange messages, and write
 * the result to a #results DOM element. Verifies:
 *
 *   1. The app starts and forks a worker (console output).
 *   2. The worker signals ready (console output).
 *   3. The #results element contains the echoed worker response.
 *   4. No fatal page errors occurred.
 *
 * Usage:  node validate-worker-fork.js <output-dir> [app-url]
 */

var puppeteer = require("puppeteer");
var fs = require("fs");
var path = require("path");

var outputDir = process.argv[2] || "./artifacts";
var appUrl = process.argv[3] || process.env.BOOTSTRAP_TEST_RUNNER_URL || "http://localhost:8000/";

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
        // Test 1: App starts and forks a worker
        // =================================================================
        console.log("\n=== Test 1: App starts and forks worker ===");

        console.log("Navigating to " + appUrl);
        await page.goto(appUrl, { waitUntil: "load", timeout: 120000 });

        // Wait for the #results element to appear (worker round-trip complete)
        var results = null;
        var retries = 45;
        while (results == null && retries-- > 0) {
            await new Promise(function (r) { setTimeout(r, 2000); });
            try {
                results = await page.$eval("#results", function (el) {
                    return el.textContent;
                });
            } catch (_e) {
                // element not yet present
            }
        }

        await page.screenshot({
            path: path.join(outputDir, "worker-fork.png"),
        });

        assert(
            results !== null && results.length > 0,
            "App produced output via #results (worker round-trip completed)"
        );

        if (results) {
            console.log("  App output: " + results.substring(0, 500));
        }

        // =================================================================
        // Test 2: Main thread logged fork initiation
        // =================================================================
        console.log("\n=== Test 2: Main thread fork log ===");

        var forkLog = consoleLogs.some(function (line) {
            return line.indexOf("Main thread: forking to worker") !== -1;
        });

        assert(forkLog, "Main thread logged 'forking to worker'");

        // =================================================================
        // Test 3: Worker started successfully
        // =================================================================
        console.log("\n=== Test 3: Worker started ===");

        var workerStarted = consoleLogs.some(function (line) {
            return line.indexOf("Worker: started") !== -1;
        });

        assert(workerStarted, "Worker logged 'Worker: started'");

        // =================================================================
        // Test 4: Worker echoed the message back
        // =================================================================
        console.log("\n=== Test 4: Worker echo response ===");

        // The #results div should contain the JSON response from the worker
        // which includes echo:true and workerResponse field.
        var hasEcho = results !== null && results.indexOf("echo") !== -1;
        var hasProcessed = results !== null && results.indexOf("processed") !== -1;

        assert(hasEcho, "Worker response contains 'echo' field");
        assert(hasProcessed, "Worker response contains 'processed' text");

        // =================================================================
        // Test 5: No fatal page errors
        // =================================================================
        console.log("\n=== Test 5: No fatal page errors ===");

        // Filter out non-fatal errors (some browsers emit benign warnings)
        var fatalErrors = pageErrors.filter(function (err) {
            // Ignore known benign errors
            return err.indexOf("favicon") === -1;
        });

        assert(
            fatalErrors.length === 0,
            "No fatal page errors" +
            (fatalErrors.length > 0
                ? " (found: " + fatalErrors[0].substring(0, 200) + ")"
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
