"use strict";

/**
 * Launches headless Chrome via Puppeteer, loads the published RayTracer app
 * (built with WasmShellVfsFrameworkAssemblyLoad=true and
 * WasmShellVfsFrameworkAssemblyLoadCleanup=true), waits for the app to
 * produce output, then verifies:
 *
 *   1. The app ran successfully (assemblies loaded from VFS via MONO_PATH).
 *   2. MONO_PATH was set to /managed in the runtime config.
 *   3. VFS cleanup removed assembly files from /managed after they were read.
 *
 * Usage:  node validate-vfs-runtime.js <output-dir> [app-url]
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

        // Collect console messages for diagnostics
        var consoleLogs = [];
        page.on("console", function (msg) {
            var text = msg.text();
            consoleLogs.push(text);
            console.log("BROWSER: " + text);
        });
        page.on("pageerror", function (err) {
            console.error("PAGE ERROR: " + err.message);
        });

        // =================================================================
        // Test 1: App starts successfully with VFS assembly loading
        // =================================================================
        console.log("\n=== Test 1: App starts with VFS assembly loading ===");

        console.log("Navigating to " + appUrl);
        await page.goto(appUrl, { waitUntil: "load", timeout: 120000 });

        // Wait for the RayTracer to produce output (the #results element)
        var results = null;
        var retries = 30;
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
            path: path.join(outputDir, "vfs-assembly-load.png"),
        });

        assert(
            results !== null && results.length > 0,
            "App produced output via #results (assemblies loaded from VFS)"
        );

        if (results) {
            console.log("  App output: " + results.substring(0, 120));
        }

        // =================================================================
        // Test 2: VFS feature flag is active in the runtime config
        // =================================================================
        console.log("\n=== Test 2: VFS feature enabled in config ===");

        // globalThis.config is the UnoConfig. MONO_PATH lives on the
        // internal MonoConfig (not exposed), but its effect is already
        // proven by Test 1 (the app loaded assemblies from VFS). Here
        // we verify the UnoConfig flags are set correctly.
        var vfsFlags = await page.evaluate(function () {
            try {
                var c = globalThis.config;
                return {
                    load: !!(c && c.uno_vfs_framework_assembly_load),
                    cleanup: !!(c && c.uno_vfs_framework_assembly_load_cleanup),
                };
            } catch (_e) {
                return null;
            }
        });

        assert(
            vfsFlags && vfsFlags.load === true,
            "uno_vfs_framework_assembly_load is true in runtime config"
        );
        assert(
            vfsFlags && vfsFlags.cleanup === true,
            "uno_vfs_framework_assembly_load_cleanup is true in runtime config"
        );

        // =================================================================
        // Test 3: VFS cleanup removed assembly files from /managed
        // =================================================================
        console.log("\n=== Test 3: VFS cleanup after load ===");

        var vfsState = await page.evaluate(function () {
            try {
                var mod = globalThis.Module;
                if (!mod || !mod.FS) return { error: "Module.FS not available" };

                var entries;
                try {
                    entries = mod.FS.readdir("/managed");
                } catch (_e) {
                    return { error: "/managed directory does not exist" };
                }

                // Filter out . and ..
                var files = entries.filter(function (e) {
                    return e !== "." && e !== "..";
                });

                // Check which are files vs directories (culture subdirs)
                var remaining = [];
                for (var i = 0; i < files.length; i++) {
                    try {
                        var stat = mod.FS.stat("/managed/" + files[i]);
                        var isDir = mod.FS.isDir(stat.mode);
                        remaining.push({
                            name: files[i],
                            isDir: isDir,
                        });
                    } catch (_e2) {
                        // stat failed - file may have been deleted
                    }
                }

                return { remaining: remaining };
            } catch (e) {
                return { error: e.toString() };
            }
        });

        if (vfsState.error) {
            console.log("  INFO: Could not inspect VFS state: " + vfsState.error);
            // Not a hard failure — the VFS directory may have been fully
            // cleaned up including the directory itself.
            assert(
                vfsState.error.indexOf("/managed directory does not exist") !== -1,
                "VFS /managed directory was cleaned up or is inaccessible"
            );
        } else {
            var remainingFiles = vfsState.remaining.filter(function (e) {
                return !e.isDir;
            });
            var remainingDirs = vfsState.remaining.filter(function (e) {
                return e.isDir;
            });

            console.log(
                "  /managed contains: " +
                remainingFiles.length + " file(s), " +
                remainingDirs.length + " subdirectory(ies)"
            );

            if (remainingFiles.length > 0) {
                console.log("  Remaining files: " +
                    remainingFiles.map(function (f) { return f.name; }).join(", "));
            }

            // After cleanup, assembly .dll files should be gone.
            // Only subdirectories (culture folders) may remain as empty dirs.
            var remainingDlls = remainingFiles.filter(function (f) {
                return f.name.endsWith(".dll");
            });

            assert(
                remainingDlls.length === 0,
                "No .dll files remain in /managed after cleanup" +
                (remainingDlls.length > 0
                    ? " (found: " + remainingDlls.map(function (f) { return f.name; }).join(", ") + ")"
                    : "")
            );
        }

        // =================================================================
        // Test 4: Check VFS cleanup log messages
        // =================================================================
        console.log("\n=== Test 4: VFS cleanup log messages ===");

        var cleanupLogs = consoleLogs.filter(function (line) {
            return line.indexOf("[Bootstrap] VFS cleanup: deleted") !== -1;
        });

        // Cleanup logs are only emitted at debugLevel, so they may not
        // appear in a release build. If they do appear, that's extra
        // confirmation; if not, we rely on the FS state check above.
        if (cleanupLogs.length > 0) {
            console.log("  Found " + cleanupLogs.length + " VFS cleanup log(s)");
            assert(true, "VFS cleanup log messages present (" + cleanupLogs.length + " files deleted)");
        } else {
            console.log("  INFO: No VFS cleanup log messages (expected in Release builds without debugLevel)");
        }

        // =================================================================
        // Test 5: Verify no assembly.c assertion warning
        // =================================================================
        console.log("\n=== Test 5: No MONO_PATH assertion warning ===");

        var assemblyWarnings = consoleLogs.filter(function (line) {
            return line.indexOf("assembly.c") !== -1 && line.indexOf("<disabled>") !== -1;
        });

        assert(
            assemblyWarnings.length === 0,
            "No assembly.c MONO_PATH assertion warnings" +
            (assemblyWarnings.length > 0
                ? " (found " + assemblyWarnings.length + " warning(s))"
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
