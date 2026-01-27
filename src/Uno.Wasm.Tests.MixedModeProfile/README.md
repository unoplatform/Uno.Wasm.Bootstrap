# Uno.Wasm.Tests.MixedModeProfile

This test project validates that `MonoRuntimeMixedModeExcludedAssembly` works independently without requiring `WasmShellAOTProfileExcludedMethods` to be specified.

## Purpose

This test was created to validate the fix for the issue where `MonoRuntimeMixedModeExcludedAssembly` was not being taken into account if `WasmShellAOTProfileExcludedMethods` was not specified.

## How It Works

1. The project uses Mixed Mode AOT compilation (`WasmShellMonoRuntimeExecutionMode=InterpreterAndAOT`)
2. It specifies `MonoRuntimeMixedModeExcludedAssembly` items to exclude certain assemblies from AOT:
   - `Newtonsoft.Json`
   - `System.Xml`
3. It deliberately does NOT specify `WasmShellAOTProfileExcludedMethods`
4. It enables `WasmShellGenerateAOTProfileDebugList=true` to generate debug dump files
5. A post-build target validates that:
   - Both `AOTProfileDump.Original.txt` and `AOTProfileDump.Filtered.txt` are generated
   - Methods from `Newtonsoft.Json` exist in the original profile
   - Methods from `Newtonsoft.Json` do NOT exist in the filtered profile
   - Methods from `System.Xml` do NOT exist in the filtered profile

## Expected Behavior

When the project builds successfully:
- `AOTProfileDump.Original.txt` is created with all profile methods including those from excluded assemblies
- `AOTProfileDump.Filtered.txt` is created with methods from excluded assemblies removed
- The validation verifies that specific assemblies are actually filtered out, not just that files exist

## Validation

The post-build target `ValidateMixedModeProfileFiltering` will fail the build if:
- The original dump file is not generated
- The filtered dump file is not generated
- No methods from `Newtonsoft.Json` are found in the original profile (test setup issue)
- Any methods from `Newtonsoft.Json` or `System.Xml` are found in the filtered profile (filtering not working)

## Running the Test

To test this manually:

```bash
cd src/Uno.Wasm.Tests.MixedModeProfile
dotnet publish -c Release
```

Check for the generated files in the `obj/Release/net10.0/browser-wasm/` directory:
- `AOTProfileDump.Original.txt` - should contain methods from all assemblies
- `AOTProfileDump.Filtered.txt` - should NOT contain methods from Newtonsoft.Json or System.Xml
