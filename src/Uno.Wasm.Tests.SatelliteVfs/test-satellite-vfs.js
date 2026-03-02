#!/usr/bin/env node
// Unit tests for the satellite resource → VFS entry conversion logic in Bootstrapper.ts.
// Two code paths are covered:
//
//   applyConfigLoaded  – mirrors the satellite fix in configLoaded().
//                        Active when uno_vfs_framework_assembly_load = false.
//
//   applyRedirectAssembliesToVfs – mirrors redirectAssembliesToVfs().
//                        Active when uno_vfs_framework_assembly_load = true.
//                        This is the root-cause path: it moves satellite entries
//                        to VFS (emptying the culture arrays) WITHOUT the culture
//                        prefix in `name` before the fix was applied.
//
// Both must stay in sync with their counterparts in Bootstrapper.ts.

"use strict";

const assert = require("assert");

// ----- extracted logic: configLoaded() satellite fix -----
// (uno_vfs_framework_assembly_load = false path)

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

// ----- extracted logic: redirectAssembliesToVfs() -----
// (uno_vfs_framework_assembly_load = true path)
// Only the satellite section and the shared moveArrayToVfs helper are exercised here;
// the regular assembly/pdb sections are omitted for brevity.

function applyRedirectAssembliesToVfs(config) {
	const res = config.resources;
	if (!res) return;

	if (!Array.isArray(res.vfs)) {
		res.vfs = [];
	}

	const moveArrayToVfs = (source, vfsDir, keepPredicate, namePrefix) => {
		if (!Array.isArray(res.vfs)) return undefined;
		if (!Array.isArray(source)) return undefined;

		const kept = [];
		for (const entry of source) {
			if (keepPredicate && keepPredicate(entry)) {
				kept.push(entry);
			} else {
				const vfsEntry = { ...entry };
				if (namePrefix) {
					// For satellite resources: download URL is _framework/{culture}/{fingerprinted}.wasm,
					// so the name must carry the culture prefix for the runtime's URL resolver.
					vfsEntry.name = namePrefix + "/" + vfsEntry.name;
				}
				const originalVirtualPath = entry.virtualPath || entry.name;
				// MONO_PATH probing looks for .dll; .NET 10+ uses .wasm (WebCIL).
				const vfsFileName = originalVirtualPath.replace(/\.wasm$/, ".dll");
				vfsEntry.virtualPath = vfsDir + "/" + vfsFileName;
				res.vfs.push(vfsEntry);
			}
		}
		return kept;
	};

	if (res.satelliteResources) {
		for (const culture in res.satelliteResources) {
			if (res.satelliteResources.hasOwnProperty(culture)) {
				const newSat = moveArrayToVfs(
					res.satelliteResources[culture],
					"/managed/" + culture,
					undefined,
					culture
				);
				if (newSat !== undefined) {
					res.satelliteResources[culture] = newSat;
				}
			}
		}
	}
}

// ----- test helpers -----

let passed = 0;
let failed = 0;

function section(title) {
	console.log(`\n${title}`);
}

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

// ===== Section 1: configLoaded satellite fix (uno_vfs_framework_assembly_load = false) =====

section("configLoaded satellite fix (uno_vfs_framework_assembly_load = false)");

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
	const config = { resources: {} };
	applyConfigLoaded(config);
	assert.strictEqual(config.resources.vfs, undefined, "vfs should not be created");
});

test("When_OriginalEntryNotMutated_Then_SourcePreserved", () => {
	const originalEntry = { name: "App.resources.abc.wasm", virtualPath: "App.resources.dll", hash: "sha256-x" };
	const config = {
		resources: {
			satelliteResources: { cs: [originalEntry] }
		}
	};

	applyConfigLoaded(config);

	assert.strictEqual(originalEntry.name, "App.resources.abc.wasm", "original entry name must not change");
	assert.strictEqual(originalEntry.virtualPath, "App.resources.dll", "original entry virtualPath must not change");
});

// ===== Section 2: redirectAssembliesToVfs satellite fix (uno_vfs_framework_assembly_load = true) =====

section("redirectAssembliesToVfs satellite fix (uno_vfs_framework_assembly_load = true)");

test("When_SingleCulture_Then_NameHasCulturePrefix", () => {
	const config = {
		resources: {
			satelliteResources: {
				cs: [
					{ name: "MSTest.TestFramework.resources.o7wkhc6u5w.wasm", virtualPath: "MSTest.TestFramework.resources.dll", hash: "sha256-abc" }
				]
			}
		}
	};

	applyRedirectAssembliesToVfs(config);

	const vfs = config.resources.vfs;
	assert.strictEqual(vfs.length, 1);

	const entry = vfs[0];
	assert.strictEqual(entry.name, "cs/MSTest.TestFramework.resources.o7wkhc6u5w.wasm",
		"name must be prefixed with culture so the runtime fetches _framework/cs/…");
	assert.strictEqual(entry.virtualPath, "/managed/cs/MSTest.TestFramework.resources.dll",
		"virtualPath should use .dll extension under /managed/cs/");
	assert.strictEqual(entry.hash, "sha256-abc", "hash should be preserved");
});

test("When_WasmExtensionInNameFallback_Then_VirtualPathUsesDll", () => {
	// When virtualPath is absent the name is used as fallback; .wasm must be
	// converted to .dll so MONO_PATH probing (assembly.c) can find the file.
	const config = {
		resources: {
			satelliteResources: {
				cs: [{ name: "App.resources.xyz.wasm", hash: "sha256-abc" }]
			}
		}
	};

	applyRedirectAssembliesToVfs(config);

	const entry = config.resources.vfs[0];
	assert.strictEqual(entry.name, "cs/App.resources.xyz.wasm",
		"name should still carry the .wasm fingerprinted form with culture prefix");
	assert.strictEqual(entry.virtualPath, "/managed/cs/App.resources.xyz.dll",
		"virtualPath must use .dll (converted from .wasm fallback) for MONO_PATH probing");
});

test("When_MultipleCultures_Then_EachGetsOwnPrefix", () => {
	const config = {
		resources: {
			satelliteResources: {
				cs: [{ name: "App.resources.aaa.wasm", virtualPath: "App.resources.dll", hash: "sha256-cs" }],
				fr: [{ name: "App.resources.bbb.wasm", virtualPath: "App.resources.dll", hash: "sha256-fr" }]
			}
		}
	};

	applyRedirectAssembliesToVfs(config);

	const vfs = config.resources.vfs;
	assert.strictEqual(vfs.length, 2);

	const cs = vfs.find(e => e.name.startsWith("cs/"));
	const fr = vfs.find(e => e.name.startsWith("fr/"));
	assert.ok(cs, "cs entry should exist");
	assert.ok(fr, "fr entry should exist");
	assert.strictEqual(cs.virtualPath, "/managed/cs/App.resources.dll");
	assert.strictEqual(fr.virtualPath, "/managed/fr/App.resources.dll");
});

test("When_SatelliteRedirected_Then_CultureArrayEmptied", () => {
	// redirectAssembliesToVfs replaces each culture array with the kept entries
	// (empty when there is no keepPredicate). The configLoaded fix must find
	// these empty arrays and produce no duplicate VFS entries.
	const config = {
		resources: {
			satelliteResources: {
				cs: [
					{ name: "App.resources.aaa.wasm", virtualPath: "App.resources.dll", hash: "sha256-1" },
					{ name: "Lib.resources.bbb.wasm", virtualPath: "Lib.resources.dll", hash: "sha256-2" }
				]
			}
		}
	};

	applyRedirectAssembliesToVfs(config);

	assert.deepStrictEqual(config.resources.satelliteResources.cs, [],
		"culture array should be emptied after redirect so the configLoaded fix is a no-op");
	assert.strictEqual(config.resources.vfs.length, 2, "both entries should be in vfs");
});

test("When_RegularAssembliesPresent_Then_NoNamePrefixApplied", () => {
	// The namePrefix must only be applied to satellite entries, not to regular
	// assembly entries (which go to /managed without a culture subdirectory).
	const config = {
		resources: {
			assembly: [
				{ name: "MyApp.abc.wasm", virtualPath: "MyApp.dll", hash: "sha256-app" }
			],
			satelliteResources: {
				cs: [{ name: "MyApp.resources.xyz.wasm", virtualPath: "MyApp.resources.dll", hash: "sha256-sat" }]
			}
		}
	};

	// Simulate the full redirectAssembliesToVfs call including the assembly section.
	const res = config.resources;
	res.vfs = [];
	const vfsManagedDir = "/managed";

	const moveArrayToVfs = (source, vfsDir, keepPredicate, namePrefix) => {
		if (!Array.isArray(res.vfs) || !Array.isArray(source)) return undefined;
		const kept = [];
		for (const entry of source) {
			if (keepPredicate && keepPredicate(entry)) {
				kept.push(entry);
			} else {
				const vfsEntry = { ...entry };
				if (namePrefix) {
					vfsEntry.name = namePrefix + "/" + vfsEntry.name;
				}
				const originalVirtualPath = entry.virtualPath || entry.name;
				const vfsFileName = originalVirtualPath.replace(/\.wasm$/, ".dll");
				vfsEntry.virtualPath = vfsDir + "/" + vfsFileName;
				res.vfs.push(vfsEntry);
			}
		}
		return kept;
	};

	const newAssembly = moveArrayToVfs(res.assembly, vfsManagedDir);  // no namePrefix
	if (newAssembly !== undefined) res.assembly = newAssembly;

	for (const culture in res.satelliteResources) {
		if (res.satelliteResources.hasOwnProperty(culture)) {
			const newSat = moveArrayToVfs(res.satelliteResources[culture], vfsManagedDir + "/" + culture, undefined, culture);
			if (newSat !== undefined) res.satelliteResources[culture] = newSat;
		}
	}

	const assemblyEntry = res.vfs.find(e => e.virtualPath === "/managed/MyApp.dll");
	const satEntry = res.vfs.find(e => e.virtualPath === "/managed/cs/MyApp.resources.dll");

	assert.ok(assemblyEntry, "regular assembly entry should be in vfs");
	assert.strictEqual(assemblyEntry.name, "MyApp.abc.wasm",
		"regular assembly name must NOT have a culture prefix");
	assert.ok(satEntry, "satellite entry should be in vfs");
	assert.strictEqual(satEntry.name, "cs/MyApp.resources.xyz.wasm",
		"satellite name must have culture prefix");
});

test("When_OriginalSatelliteEntryNotMutated_Then_SourcePreserved", () => {
	const originalEntry = { name: "App.resources.abc.wasm", virtualPath: "App.resources.dll", hash: "sha256-x" };
	const config = {
		resources: {
			satelliteResources: { cs: [originalEntry] }
		}
	};

	applyRedirectAssembliesToVfs(config);

	assert.strictEqual(originalEntry.name, "App.resources.abc.wasm",
		"redirectAssembliesToVfs must not mutate the source entry (uses spread copy)");
	assert.strictEqual(originalEntry.virtualPath, "App.resources.dll");
});

// ===== Section 3: Interaction — redirectAssembliesToVfs runs before configLoaded fix =====

section("Interaction: redirectAssembliesToVfs → configLoaded (no duplicates)");

test("When_VfsFrameworkLoadEnabled_Then_ConfigLoadedFixIsNoOp", () => {
	// This is the root-cause scenario:
	//   1. redirectAssembliesToVfs moves satellite entries to VFS and empties the arrays.
	//   2. configLoaded's satellite fix runs but finds empty arrays → produces no additional entries.
	// Result: exactly the entries from step 1, each with the correct culture-prefixed name.
	const config = {
		resources: {
			satelliteResources: {
				cs: [
					{ name: "MSTest.TestFramework.resources.o7wkhc6u5w.wasm", virtualPath: "MSTest.TestFramework.resources.dll", hash: "sha256-cs" }
				]
			}
		}
	};

	applyRedirectAssembliesToVfs(config);  // runs first (uno_vfs_framework_assembly_load = true)
	applyConfigLoaded(config);              // runs second — must be a no-op

	const vfs = config.resources.vfs;
	assert.strictEqual(vfs.length, 1, "configLoaded must not produce duplicate entries");
	assert.strictEqual(vfs[0].name, "cs/MSTest.TestFramework.resources.o7wkhc6u5w.wasm",
		"the single entry must have the culture-prefixed name from redirectAssembliesToVfs");
});

test("When_VfsFrameworkLoadDisabled_Then_ConfigLoadedFixHandlesSatellites", () => {
	// When redirectAssembliesToVfs is NOT called, configLoaded's fix is the only
	// code path and must produce correct VFS entries on its own.
	const config = {
		resources: {
			satelliteResources: {
				cs: [
					{ name: "MSTest.TestFramework.resources.o7wkhc6u5w.wasm", virtualPath: "MSTest.TestFramework.resources.dll", hash: "sha256-cs" }
				]
			}
		}
	};

	// Only configLoaded runs (no redirectAssembliesToVfs)
	applyConfigLoaded(config);

	const vfs = config.resources.vfs;
	assert.ok(vfs, "vfs must be created by configLoaded fix");
	assert.strictEqual(vfs.length, 1);
	assert.strictEqual(vfs[0].name, "cs/MSTest.TestFramework.resources.o7wkhc6u5w.wasm",
		"configLoaded fix must produce culture-prefixed name");
});

// ----- summary -----

console.log(`\n${passed} passed, ${failed} failed`);
if (failed > 0) {
	process.exit(1);
}
