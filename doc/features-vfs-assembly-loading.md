---
uid: UnoWasmBootstrap.Features.VfsAssemblyLoading
---

# VFS Framework Assembly Loading

By default the .NET WebAssembly runtime loads assemblies as in-memory bundled resources via `mono_wasm_add_assembly`. Each `AssemblyLoadContext` (ALC) that loads the same assembly receives its own `MonoImage` copy, which can lead to significant memory duplication in applications that create multiple ALCs.

When VFS Framework Assembly Loading is enabled, assemblies are placed in the Emscripten virtual filesystem (VFS) instead of being registered as bundled resources. The Mono runtime's filename-based image cache (`mono_image_open_a_lot`) then deduplicates `MonoImage` instances across ALCs: the first load reads the file from the VFS, and subsequent loads of the same path return the cached image without allocating additional memory.

## Enabling VFS assembly loading

Add the following to your `.csproj`:

```xml
<PropertyGroup>
    <WasmShellVfsFrameworkAssemblyLoad>true</WasmShellVfsFrameworkAssemblyLoad>
</PropertyGroup>
```

At startup, the bootstrapper:

1. Moves assembly entries from `config.resources.assembly` to `config.resources.vfs["/managed"]`, except for the main assembly which stays as a bundled resource so the runtime can resolve the entry point directly.
2. Moves PDB entries from `config.resources.pdb` to the same VFS path.
3. Moves satellite resource assemblies from `config.resources.satelliteResources` to `config.resources.vfs["/managed/<culture>"]`.
4. Sets the `MONO_PATH` environment variable to `/managed` so the runtime probes that directory when resolving assemblies.

## Cleaning up VFS files after load

Once the Mono runtime reads an assembly file from the VFS, the bytes are held in the in-memory image cache. The VFS copy is no longer needed and only consumes additional memory. Enabling VFS cleanup deletes each assembly file from the Emscripten VFS as soon as the runtime closes it, reclaiming that memory.

```xml
<PropertyGroup>
    <WasmShellVfsFrameworkAssemblyLoad>true</WasmShellVfsFrameworkAssemblyLoad>
    <WasmShellVfsFrameworkAssemblyLoadCleanup>true</WasmShellVfsFrameworkAssemblyLoadCleanup>
</PropertyGroup>
```

The cleanup hooks into Emscripten's `FS.trackingDelegate['onCloseFile']` callback. Because the Mono runtime performs a synchronous open-read-close cycle when loading assemblies, the `onCloseFile` event fires exactly when the file is no longer needed. The image cache retains the `MonoImage` by filename, so future ALC loads of the same assembly still hit the cache without accessing the filesystem.

> [!NOTE]
> Satellite resource assemblies under `/managed/<culture>/` are also cleaned up. Files that have not yet been loaded (e.g., assemblies loaded lazily via reflection) remain in the VFS until the runtime reads them.

## MSBuild properties

| Property | Default | Description |
|---|---|---|
| `WasmShellVfsFrameworkAssemblyLoad` | `false` | Place framework assemblies in the Emscripten VFS instead of loading them as bundled resources. |
| `WasmShellVfsFrameworkAssemblyLoadCleanup` | `false` | Delete assembly files from the VFS after the runtime closes them. Requires `WasmShellVfsFrameworkAssemblyLoad`. |
