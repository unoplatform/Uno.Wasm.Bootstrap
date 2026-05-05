# WebWorker Mode — Progress

**Last updated**: 2026-05-04

## Completed

- [x] `ShellMode.WebWorker` enum value
- [x] `GenerateWorkerJs()` in `ShellTask.cs` — generates `worker.js` + `index.html`
- [x] `WorkerFileName` property on `ShellTask_v0`
- [x] MSBuild: `WasmShellWorkerFileName`, `WasmShellWorkerBasePath` properties
- [x] MSBuild: `_UnoBuildAndImportWebWorkerAssets` target — builds worker project, reads its manifest via `GetCurrentProjectBuildStaticWebAssetItems`, re-emits the worker's assets as `Content` items rooted under `$(WasmShellWorkerBasePath)` so they flow through Bootstrap's package-folder rewriter (`%PACKAGE%` → `package_<hostHash>/<WasmShellWorkerBasePath>/`)
- [x] MSBuild: `_UnoPublishWebWorkerFramework` target — publishes the worker's `_framework/` directory and copies it into `wwwroot/package_<hostHash>/<WasmShellWorkerBasePath>/_framework/`, then fixes up the `dotnet.js` fingerprint in the worker's `uno-config.js`
- [x] Versioned worker URL: worker placed inside the host's hashed package folder so its URL is implicitly versioned by the host's content hash (prevents v1-host/v2-worker skew during rolling deploys)
- [x] MSBuild: Hot Reload auto-disabled for WebWorker mode
- [x] MSBuild: Service worker skipped for WebWorker mode
- [x] Config loading: fetch+eval with `let config → var config` transform (so the identifier survives inside `new Function(...)`)
- [x] `invokeJS` shim on `globalThis` for `[JSImport]` interop
- [x] `self.onmessage` not set before `dotnet.create()` (runtime safety)
- [x] Sample: `Uno.Wasm.Tests.WebWorker.App` (worker project)
- [x] Sample: `Uno.Wasm.Tests.WebWorker.Host` (host project, uses `WasmShellWebWorkerProject`)
- [x] Test: Puppeteer validation (`validate-webworker.js`)
- [x] Test: CI shell script (`test-webworker.sh`)
- [x] CI: linux02 job in `stage-build-linux-tests.yml`
- [x] Documentation: `doc/features-webworker-mode.md`
- [x] Documentation: `doc/toc.yml` entry
- [x] Spec: `specs/003-webworker-mode/spec.md`
- [x] Host integration via Content-item re-emission (no build-time `dotnet publish` shelling); a publish-time worker publish remains for `_framework/`
- [x] End-to-end verified: host+worker Puppeteer test passes locally

## Remaining

- [ ] Verify CI pipeline runs successfully on linux02
- [ ] Test incremental build scenarios (rebuild host without cleaning worker)
- [ ] Test with AOT compilation enabled on the worker project
- [ ] Test worker with additional NuGet package dependencies
- [ ] Investigate dev-server support for worker files during `dotnet run`
- [x] Update spec to reflect the Content-item route + package-folder placement

## Host Integration — How It Works

The `_UnoBuildAndImportWebWorkerAssets` target in `Uno.Wasm.Bootstrap.targets` runs `BeforeTargets="AssignTargetPaths"` when `WasmShellWebWorkerProject` is set:

1. **Build** the worker project via `MSBuild` task (`Restore;Build;GetCurrentProjectBuildStaticWebAssetItems`)
2. **Read** the returned items, filter by `ResultType=StaticWebAsset`
3. **Strip the SDK's fingerprint placeholder** (`#[.{fingerprint}]?`) from each manifest item's `RelativePath` to produce a clean target path
4. **Re-emit each manifest asset as a `Content` item** with:
   - `Link` and `TargetPath` set to `$(WasmShellWorkerBasePath)\<cleanRelativePath>`
   - `CopyToOutputDirectory=PreserveNewest` and `CopyToPublishDirectory=PreserveNewest`
   - Item batching syntax (`Include="%(_UnoWorkerManifestAsset.Identity)"`) so the items carry only Content metadata — they do **not** inherit the `ResultType=StaticWebAsset` metadata that would cause downstream targets to filter them out
5. Bootstrap's existing `GeneratePackageFolder` step rewrites `%PACKAGE%` in target paths to `package_<hostHash>/`, so the worker assets land at `wwwroot/package_<hostHash>/<WasmShellWorkerBasePath>/...`
6. **Publish-time `_framework/` copy** (`_UnoPublishWebWorkerFramework`, `AfterTargets="Publish"`):
   - Resolves the worker's TFM via `Targets="_UnoGetTargetFramework"` (no hardcoded `net10.0`)
   - Publishes the worker (`Targets="Publish"`) and copies its `_framework/` into `wwwroot/package_<hostHash>/<WasmShellWorkerBasePath>/_framework/`
   - Fixes up the `dotnet.js` fingerprint reference in the worker's `uno-config.js` so it matches the actual fingerprinted file in `_framework/`

No `dotnet publish` shelling at build time, no `RemoveDir` hacks, no `StaticWebAsset` registration. The Content-item route works with any SDK version because it relies only on the standard `<Content>` plumbing.

## Why The Worker Lives Inside `package_<hostHash>/`

Earlier iterations placed the worker at the top of `wwwroot/` (e.g. `_worker/worker.js`). That URL is **not versioned** — during a rolling deployment a v1-host page can fetch a v2-worker (or vice versa), pairing mismatched bytes. Putting the worker under the host's hashed package folder makes its URL implicitly versioned: a v1 host reaches for `/package_<hostV1>/worker/worker.js`, which either still exists on disk (and serves v1 worker bytes) or 404s. There is no path through which v1 host bytes can silently load v2 worker bytes from the same URL.

The host page resolves the URL via `config.uno_app_base` (which already includes the hash):

```javascript
const appBase = (globalThis.config && globalThis.config.uno_app_base) || '.';
const worker = new Worker(appBase + '/worker/worker.js');
```

## Known Limitations

1. **Hybrid pipeline**: Non-framework assets (worker.js, uno-config.js, etc.) flow through the manifest at build time as Content items — no fingerprint fixup needed at that stage. `_framework/` files are only produced by `WasmTriggerPublishApp` during publish, so the publish-time copy step is still required. The fingerprint in `uno-config.js` is fixed up after the publish-time copy.

2. ~~**Hardcoded TFM path**~~ — resolved. The worker's TFM is retrieved by `_UnoGetTargetFramework` (invoked via `Targets="_UnoGetTargetFramework"` against the worker project) and stored in `_UnoWorkerTargetFramework`, which feeds `_UnoWorkerPublishWwwroot`.

## Investigation Log

### Static Web Assets Pipeline Attempts

Six approaches were tried before arriving at the current Content-item route:

| # | Approach | Build | Publish | Issue |
|---|---|---|---|---|
| 1 | `StaticWebAssetBasePath=_worker` (Root mode) | OK | Fail | `GeneratePublishWasmBootJson` can't resolve endpoints for the worker's `_framework/` |
| 2 | `StaticWebAssetProjectMode=Default` + `StaticWebAssetBasePath` | OK | OK | Assets not copied to publish output (Default = dev-server only) |
| 3 | `DefineStaticWebAssets` with `BasePath` on raw file glob | OK | Fail | SDK conflict detection compares `RelativePath` ignoring `BasePath` |
| 4 | Explicit `dotnet publish` + file copy (early `_UnoPublishWebWorkerProject` prototype) | OK | OK | Works but requires `RemoveDir` hack for stale incremental state, shells out to `dotnet publish` at build time |
| 5 | Manifest-based: `GetCurrentProjectBuildStaticWebAssetItems` + `DefineStaticWebAssets` registration | OK | OK | Worker landed at top-level `_worker/` — URL not versioned by host hash, exposed to v1-host/v2-worker skew during rolling deploys. Also required `ApplyCompressionNegotiation` clones for compressed alternatives. |
| 6 | **Content items** (current): re-emit each manifest asset as a `<Content>` item rooted under `$(WasmShellWorkerBasePath)`, run `BeforeTargets="AssignTargetPaths"` so Bootstrap's `%PACKAGE%` rewriter places it inside `package_<hostHash>/` | OK | OK | Worker URL is now versioned by the host's content hash. No StaticWebAsset registration, no `ApplyCompressionNegotiation` required, no SDK-internal items leaked downstream. |

Key discoveries:

- `GetCurrentProjectBuildStaticWebAssetItems` returns ALL assets including those from referenced projects, with proper metadata
- The SDK fingerprint placeholder (`#[.{fingerprint}]?`) leaks literally into Content `Link`/`TargetPath` if not stripped — pre-compute a `CleanRelativePath` metadata via regex first
- Item batching (`Include="%(_UnoWorkerManifestAsset.Identity)"`) is required: a reference-style include (`Include="@(_UnoWorkerManifestAsset)"`) inherits `ResultType=StaticWebAsset` metadata, and downstream targets (e.g. `AssignTargetPaths`) filter those Content items out — confirmed via DEBUG instrumentation showing Content count going `0 → 8 → 0`
- Running the target `BeforeTargets="AssignTargetPaths"` (not `ResolveStaticWebAssetsInputs`) makes the items visible to Bootstrap's package-folder rewriter, which is what places them inside `package_<hostHash>/`
- For approach 5 (kept here as historical context): `WasmResource`/`Culture` traits had to be cleared to prevent the Blazor SDK's boot JSON generator from processing worker assets; `CopyToPublishDirectory` had to be set to `PreserveNewest`; `AssetKind` had to be set to `All`; `Content-Encoding` traits had to be preserved for compressed-alternative validation. The Content-item route bypasses all of this because it doesn't go through the SDK's StaticWebAsset registration at all.
