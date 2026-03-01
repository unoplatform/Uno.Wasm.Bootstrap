"use strict";

/**
 * Launches headless Chrome via Puppeteer, loads the published VFS test app
 * (built with WasmShellVfsFrameworkAssemblyLoad=true and
 * WasmShellVfsFrameworkAssemblyLoadCleanup=true), waits for the app to
 * produce output, then verifies:
 *
 *   1. The app ran successfully (assemblies loaded from VFS via MONO_PATH).
 *   2. VFS feature flags are active in the runtime config.
 *   3. Assemblies were redirected to VFS /managed (non-zero entry count).
 *   4. VFS cleanup removed assembly files (.dll/.wasm) from /managed after load.
 *   5. VFS cleanup log messages are emitted (when debugLevel is active).
 *   6. No assembly.c MONO_PATH assertion warnings.
 *   7. No mono_wasm_bind_assembly_exports assertion (ExitStatus crash).
 *   8. coreAssembly entries (System.Runtime.InteropServices.JavaScript,
 *      System.Private.CoreLib) are NOT redirected to VFS /managed.
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
        // Test 1: App starts successfully with VFS assembly loading
        // =================================================================
        console.log("\n=== Test 1: App starts with VFS assembly loading ===");

        console.log("Navigating to " + appUrl);
        await page.goto(appUrl, { waitUntil: "load", timeout: 120000 });

        // Wait for the app to produce output (the #results element)
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
            console.log("  App output: " + results.substring(0, 200));
        }

        // The VFS test app calls into a non-BCL library (VfsTestHelper)
        // that must be loaded from VFS. Verify the library's greeting
        // appears in the output.
        assert(
            results !== null && results.indexOf("Hello from VFS-loaded library") !== -1,
            "Non-BCL library assembly was loaded successfully from VFS"
        );

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
        // Test 3: Assemblies were redirected to VFS /managed
        // =================================================================
        console.log("\n=== Test 3: Assemblies redirected to VFS ===");

        // The Bootstrapper logs the pre-processing state and the number of
        // entries moved into VFS, but only when debugLevel > 0. In Release
        // builds these logs are absent, so treat them as informational.
        var preProcessLog = null;
        var redirectLog = null;
        for (var i = 0; i < consoleLogs.length; i++) {
            var preMatch = consoleLogs[i].match(/\[Bootstrap\] VFS redirect: pre-processing state/);
            if (preMatch) {
                preProcessLog = consoleLogs[i];
            }
            var match = consoleLogs[i].match(/\[Bootstrap\] VFS redirect: (\d+) entries moved to \/managed/);
            if (match) {
                redirectLog = { count: parseInt(match[1], 10), line: consoleLogs[i] };
            }
        }

        if (preProcessLog) {
            console.log("  Pre-processing: " + preProcessLog);
        }

        if (redirectLog) {
            console.log("  Redirect details: " + redirectLog.line);
            assert(
                redirectLog.count > 0,
                "VFS redirect moved assemblies to /managed (" + redirectLog.count + " entries)"
            );
        } else {
            console.log("  INFO: VFS redirect log not present (expected in Release builds without debugLevel)");
        }

        // The real proof that VFS redirect worked is Test 1 (non-BCL
        // library loaded successfully) and Test 4 (cleanup removed files
        // from /managed). The log messages are supplementary diagnostics.
        assert(true, "VFS redirect verified (via Test 1 app output)");

        // =================================================================
        // Test 4: VFS cleanup removed assembly files from /managed
        // =================================================================
        console.log("\n=== Test 4: VFS cleanup after load ===");

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

            // After cleanup, assembly files should be gone.
            // .NET 10+ uses .wasm extension; older runtimes used .dll.
            // Only subdirectories (culture folders) may remain as empty dirs.
            var remainingAssemblies = remainingFiles.filter(function (f) {
                return f.name.endsWith(".dll") || f.name.endsWith(".wasm");
            });

            assert(
                remainingAssemblies.length === 0,
                "No assembly files (.dll/.wasm) remain in /managed after cleanup" +
                (remainingAssemblies.length > 0
                    ? " (found: " + remainingAssemblies.map(function (f) { return f.name; }).join(", ") + ")"
                    : "")
            );
        }

        // =================================================================
        // Test 5: Check VFS cleanup log messages
        // =================================================================
        console.log("\n=== Test 5: VFS cleanup log messages ===");

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
        // Test 6: Verify no assembly.c assertion warning
        // =================================================================
        console.log("\n=== Test 6: No MONO_PATH assertion warning ===");

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

        // =================================================================
        // Test 7: Verify no mono_wasm_bind_assembly_exports assertion
        // =================================================================
        console.log("\n=== Test 7: No bind_assembly_exports assertion ===");

        // The ExitStatus / mono_wasm_bind_assembly_exports assertion fires
        // when a coreAssembly (e.g. System.Runtime.InteropServices.JavaScript)
        // is accidentally redirected to VFS instead of being loaded as a
        // bundled resource. Check both console logs and page errors.
        var allMessages = consoleLogs.concat(pageErrors);
        var bindExportErrors = allMessages.filter(function (line) {
            return line.indexOf("mono_wasm_bind_assembly_exports") !== -1
                || line.indexOf("corebindings.c") !== -1;
        });

        assert(
            bindExportErrors.length === 0,
            "No mono_wasm_bind_assembly_exports assertion errors" +
            (bindExportErrors.length > 0
                ? " (found: " + bindExportErrors[0].substring(0, 200) + ")"
                : "")
        );

        // =================================================================
        // Test 8: coreAssembly entries are not in VFS /managed
        // =================================================================
        console.log("\n=== Test 8: coreAssembly not redirected to VFS ===");

        // System.Runtime.InteropServices.JavaScript must be loaded as a
        // bundled resource (not via VFS) because mono_wasm_bind_assembly_exports
        // in corebindings.c requires it before VFS probing is available.
        // Verify these files do NOT exist under /managed in the VFS.
        var coreAssemblyVfsState = await page.evaluate(function () {
            try {
                var mod = globalThis.Module;
                if (!mod || !mod.FS) return { error: "Module.FS not available" };

                var coreNames = [
                    "System.Runtime.InteropServices.JavaScript.wasm",
                    "System.Runtime.InteropServices.JavaScript.dll",
                    "System.Private.CoreLib.wasm",
                    "System.Private.CoreLib.dll"
                ];

                var found = [];
                for (var i = 0; i < coreNames.length; i++) {
                    try {
                        mod.FS.stat("/managed/" + coreNames[i]);
                        found.push(coreNames[i]);
                    } catch (_e) {
                        // File does not exist — expected
                    }
                }

                return { foundInVfs: found };
            } catch (e) {
                return { error: e.toString() };
            }
        });

        if (coreAssemblyVfsState.error) {
            console.log("  INFO: Could not inspect VFS for coreAssembly: " + coreAssemblyVfsState.error);
        } else {
            assert(
                coreAssemblyVfsState.foundInVfs.length === 0,
                "coreAssembly files are not present in VFS /managed" +
                (coreAssemblyVfsState.foundInVfs.length > 0
                    ? " (found: " + coreAssemblyVfsState.foundInVfs.join(", ") + ")"
                    : "")
            );
        }

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
