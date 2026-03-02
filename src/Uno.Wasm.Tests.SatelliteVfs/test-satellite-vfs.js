#!/usr/bin/env node
// Unit tests for the satellite resource → VFS entry conversion logic in Bootstrapper.ts.
// Mirrors the moveArrayToVfs implementation added to configLoaded() so it can be verified
// without a browser or a full WASM publish.

"use strict";

const assert = require("assert");

// ----- extracted logic (must stay in sync with Bootstrapper.ts configLoaded) -----

function applyConfigLoaded(config) {
	const res = config.resources;
	if (res?.satelliteResources) {
		const vfsManagedDir = "/managed";

		const moveArrayToVfs = (source, vfsDir, namePrefix) => {
			if (!source) return;
			for (const entry of source) {
				const vfsEntry = { ...entry };
				if (namePrefix) {
					vfsEntry.name = namePrefix + "/" + vfsEntry.name;
				}
				vfsEntry.virtualPath = vfsDir + "/" + (entry.virtualPath || entry.name);
				res.vfs = res.vfs || [];
				res.vfs.push(vfsEntry);
			}
		};

		for (const culture in res.satelliteResources) {
			moveArrayToVfs(res.satelliteResources[culture], vfsManagedDir + "/" + culture, culture);
		}
	}
}

// ----- test helpers -----

let passed = 0;
let failed = 0;

function test(name, fn) {
	try {
		fn();
		console.log(`  ✓ ${name}`);
		passed++;
	} catch (e) {
		console.error(`  ✗ ${name}`);
		console.error(`    ${e.message}`);
		failed++;
	}
}

// ----- tests -----

console.log("Satellite resource → VFS entry conversion tests\n");

test("When_SingleCulture_Then_VfsEntryCreated", () => {
	const config = {
		resources: {
			satelliteResources: {
				cs: [
					{ name: "MSTest.TestFramework.resources.o7wkhc6u5w.wasm", virtualPath: "MSTest.TestFramework.resources.dll", hash: "sha256-abc" }
				]
			}
		}
	};

	applyConfigLoaded(config);

	const vfs = config.resources.vfs;
	assert.ok(vfs, "vfs should be created");
	assert.strictEqual(vfs.length, 1);

	const entry = vfs[0];
	assert.strictEqual(entry.name, "cs/MSTest.TestFramework.resources.o7wkhc6u5w.wasm",
		"name should be prefixed with culture");
	assert.strictEqual(entry.virtualPath, "/managed/cs/MSTest.TestFramework.resources.dll",
		"virtualPath should include managed dir and culture");
	assert.strictEqual(entry.hash, "sha256-abc", "hash should be preserved");
});

test("When_MultipleCultures_Then_AllVfsEntriesCreated", () => {
	const config = {
		resources: {
			satelliteResources: {
				cs: [{ name: "App.resources.abc.wasm", virtualPath: "App.resources.dll", hash: "sha256-cs" }],
				fr: [{ name: "App.resources.def.wasm", virtualPath: "App.resources.dll", hash: "sha256-fr" }],
				de: [{ name: "App.resources.ghi.wasm", virtualPath: "App.resources.dll", hash: "sha256-de" }]
			}
		}
	};

	applyConfigLoaded(config);

	const vfs = config.resources.vfs;
	assert.strictEqual(vfs.length, 3);

	const cs = vfs.find(e => e.name.startsWith("cs/"));
	const fr = vfs.find(e => e.name.startsWith("fr/"));
	const de = vfs.find(e => e.name.startsWith("de/"));

	assert.ok(cs, "cs entry should exist");
	assert.ok(fr, "fr entry should exist");
	assert.ok(de, "de entry should exist");

	assert.strictEqual(cs.name, "cs/App.resources.abc.wasm");
	assert.strictEqual(cs.virtualPath, "/managed/cs/App.resources.dll");

	assert.strictEqual(fr.name, "fr/App.resources.def.wasm");
	assert.strictEqual(fr.virtualPath, "/managed/fr/App.resources.dll");
});

test("When_MultipleEntriesPerCulture_Then_AllConverted", () => {
	const config = {
		resources: {
			satelliteResources: {
				cs: [
					{ name: "One.resources.aaa.wasm", virtualPath: "One.resources.dll", hash: "sha256-1" },
					{ name: "Two.resources.bbb.wasm", virtualPath: "Two.resources.dll", hash: "sha256-2" },
					{ name: "Three.resources.ccc.wasm", virtualPath: "Three.resources.dll", hash: "sha256-3" }
				]
			}
		}
	};

	applyConfigLoaded(config);

	const vfs = config.resources.vfs;
	assert.strictEqual(vfs.length, 3);
	assert.strictEqual(vfs[0].name, "cs/One.resources.aaa.wasm");
	assert.strictEqual(vfs[1].name, "cs/Two.resources.bbb.wasm");
	assert.strictEqual(vfs[2].name, "cs/Three.resources.ccc.wasm");
});

test("When_EntryHasNoVirtualPath_Then_NameUsedAsVirtualPath", () => {
	const config = {
		resources: {
			satelliteResources: {
				cs: [
					{ name: "App.resources.xyz.wasm", hash: "sha256-abc" }
					// no virtualPath field
				]
			}
		}
	};

	applyConfigLoaded(config);

	const entry = config.resources.vfs[0];
	assert.strictEqual(entry.name, "cs/App.resources.xyz.wasm");
	// Falls back to original name (without prefix) for virtualPath
	assert.strictEqual(entry.virtualPath, "/managed/cs/App.resources.xyz.wasm");
});

test("When_VfsAlreadyExists_Then_EntriesAreAppended", () => {
	const config = {
		resources: {
			vfs: [
				{ name: "existing.wasm", virtualPath: "/managed/existing.dll", hash: "sha256-pre" }
			],
			satelliteResources: {
				cs: [{ name: "New.resources.abc.wasm", virtualPath: "New.resources.dll", hash: "sha256-new" }]
			}
		}
	};

	applyConfigLoaded(config);

	const vfs = config.resources.vfs;
	assert.strictEqual(vfs.length, 2);
	assert.strictEqual(vfs[0].name, "existing.wasm", "pre-existing entry should be preserved");
	assert.strictEqual(vfs[1].name, "cs/New.resources.abc.wasm", "new entry appended");
});

test("When_NoSatelliteResources_Then_VfsNotCreated", () => {
	const config = {
		resources: {}
	};

	applyConfigLoaded(config);

	assert.strictEqual(config.resources.vfs, undefined, "vfs should not be created");
});

test("When_SatelliteResourcesNull_Then_VfsNotCreated", () => {
	const config = {
		resources: {
			satelliteResources: null
		}
	};

	applyConfigLoaded(config);

	assert.strictEqual(config.resources.vfs, undefined, "vfs should not be created");
});

test("When_OriginalEntryNotMutated_Then_SourcePreserved", () => {
	const originalEntry = { name: "App.resources.abc.wasm", virtualPath: "App.resources.dll", hash: "sha256-x" };
	const config = {
		resources: {
			satelliteResources: {
				cs: [originalEntry]
			}
		}
	};

	applyConfigLoaded(config);

	// The source entry must not be modified (spread creates a copy)
	assert.strictEqual(originalEntry.name, "App.resources.abc.wasm", "original entry name must not change");
	assert.strictEqual(originalEntry.virtualPath, "App.resources.dll", "original entry virtualPath must not change");
});

test("When_EmptyCultureArray_Then_NoVfsEntry", () => {
	const config = {
		resources: {
			satelliteResources: {
				cs: []
			}
		}
	};

	applyConfigLoaded(config);

	// vfs may be undefined or an empty array — either is acceptable
	const vfs = config.resources.vfs;
	const count = vfs ? vfs.length : 0;
	assert.strictEqual(count, 0, "no VFS entries for empty culture array");
});

// ----- summary -----

console.log(`\n${passed} passed, ${failed} failed`);
if (failed > 0) {
	process.exit(1);
}
