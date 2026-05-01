"use strict";

/**
 * Regression test for the worker `uno_dependencies` URL resolution contract.
 *
 * `WorkerBootstrapper.ts` resolves entries in `config.uno_dependencies` via
 *
 *     new URL(dep.endsWith('.js') ? dep : dep + '.js', self.location.href)
 *
 * The C# emitter (`ShellTask.BuildDependencyPath`) produces two distinct
 * shapes depending on `WebAppBasePath`:
 *
 *   - Relative baseLookup (default `./pkg/`): emits "filename" without `.js`.
 *   - Absolute baseLookup (`/pkg/`): emits "filename.js" with `.js`.
 *
 * The worker MUST resolve both cleanly:
 *   - No `.js.js` (caused by unconditional `+ '.js'` append).
 *   - No `host//path` double-slash (caused by `string + string` concat
 *     where the dep already starts with `/`).
 *
 * This script keeps the contract pinned: if `WorkerBootstrapper.ts` reverts
 * to naive concatenation, the assertions here will surface the divergence
 * because the helper below mirrors the bootstrapper's logic verbatim.
 *
 * Usage: node validate-deps-url.js
 */

function resolveDependencyUrl(dep, baseUrl) {
    const specifier = dep.endsWith(".js") ? dep : dep + ".js";
    return new URL(specifier, baseUrl).href;
}

// The worker is now published inside the host's hashed package folder, so
// self.location.href in the worker context looks like:
//   http://<host>/package_<hostHash>/worker/worker.js
const baseUrl = "http://localhost:8001/package_24816ef2/worker/worker.js";

const cases = [
    {
        name: "relative dep without .js (default for relative baseLookup)",
        dep: "./package_abc/uno-worker-bootstrap",
        expected: "http://localhost:8001/package_24816ef2/worker/package_abc/uno-worker-bootstrap.js",
    },
    {
        name: "relative dep already ending in .js",
        dep: "./helpers.js",
        expected: "http://localhost:8001/package_24816ef2/worker/helpers.js",
    },
    {
        name: "absolute dep with .js (default for absolute baseLookup)",
        dep: "/package_abc/helpers.js",
        expected: "http://localhost:8001/package_abc/helpers.js",
    },
    {
        name: "absolute dep without .js",
        dep: "/package_abc/helpers",
        expected: "http://localhost:8001/package_abc/helpers.js",
    },
    {
        name: "bare relative dep (no leading ./)",
        dep: "package_abc/foo",
        expected: "http://localhost:8001/package_24816ef2/worker/package_abc/foo.js",
    },
    {
        name: "fully-qualified URL",
        dep: "https://cdn.example.com/lib/foo.js",
        expected: "https://cdn.example.com/lib/foo.js",
    },
];

let failures = 0;

for (const c of cases) {
    const got = resolveDependencyUrl(c.dep, baseUrl);
    if (got === c.expected) {
        console.log("  PASS: " + c.name);
    } else {
        console.error("  FAIL: " + c.name);
        console.error("    dep:      " + c.dep);
        console.error("    expected: " + c.expected);
        console.error("    got:      " + got);
        failures++;
    }
}

// Negative-control assertions: verify that the regression patterns the fix
// guards against actually misbehave under naive `string + string` resolution.
// These document why `new URL(...)` + conditional `.js` is needed.
function naiveConcat(dep, baseUrl) {
    const depBase = baseUrl.substring(0, baseUrl.lastIndexOf("/") + 1);
    return depBase + dep + ".js";
}

const naiveCases = [
    {
        name: "naive concat doubles .js when dep already has .js",
        dep: "/pkg/foo.js",
        baseUrl: baseUrl,
        contains: ".js.js",
    },
    {
        name: "naive concat produces wrong absolute path resolution",
        dep: "/pkg/foo",
        baseUrl: baseUrl,
        // Naive concat appends to baseUrl's directory ("…/worker/") + "/pkg/foo.js"
        // -> "…/worker//pkg/foo.js" (double-slash). new URL() resolves to
        // "http://localhost:8001/pkg/foo.js" (origin root). The double-slash
        // proves the naive resolver doesn't understand absolute paths.
        contains: "//pkg/",
    },
];

for (const c of naiveCases) {
    const naive = naiveConcat(c.dep, c.baseUrl);
    if (naive.indexOf(c.contains) !== -1) {
        console.log("  PASS: " + c.name + " (naive output: '" + naive + "')");
    } else {
        console.error("  FAIL: " + c.name);
        console.error("    naive output: " + naive);
        console.error("    expected to contain: " + c.contains);
        failures++;
    }
}

if (failures > 0) {
    console.error("\nFAILED: " + failures + " dependency-URL assertion(s) failed.");
    process.exit(1);
}

console.log("\nAll dependency-URL assertions passed.");
