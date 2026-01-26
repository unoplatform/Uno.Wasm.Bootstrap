# Uno.Wasm.Tests.MixedModeProfile

This test project validates that `MonoRuntimeMixedModeExcludedAssembly` works independently without requiring `WasmShellAOTProfileExcludedMethods` to be specified.

## Purpose

This test was created to validate the fix for the issue where `MonoRuntimeMixedModeExcludedAssembly` was not being taken into account if `WasmShellAOTProfileExcludedMethods` was not specified.

## How It Works

1. The project uses Mixed Mode AOT compilation (`WasmShellMonoRuntimeExecutionMode=InterpreterAndAOT`)
2. It specifies `MonoRuntimeMixedModeExcludedAssembly` items to exclude certain assemblies from AOT
3. It deliberately does NOT specify `WasmShellAOTProfileExcludedMethods`
4. It enables `WasmShellGenerateAOTProfileDebugList=true` to generate debug dump files
5. A post-build target validates that both `AOTProfileDump.Original.txt` and `AOTProfileDump.Filtered.txt` are generated

## Expected Behavior

When the project builds successfully:
- `AOTProfileDump.Original.txt` should be created with the original profile methods
- `AOTProfileDump.Filtered.txt` should be created with methods from excluded assemblies removed
- The build should complete without errors

## Validation

The post-build target `ValidateMixedModeProfileFiltering` will fail the build if:
- The original dump file is not generated
- The filtered dump file is not generated (which would indicate the fix is not working)

## Running the Test

To test this manually:

```bash
cd src/Uno.Wasm.Tests.MixedModeProfile
dotnet publish -c Release
```

Check for the generated files in the `obj/Release/net10.0/browser-wasm/` directory:
- `AOTProfileDump.Original.txt`
- `AOTProfileDump.Filtered.txt`
