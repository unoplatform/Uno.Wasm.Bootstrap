---
uid: Uno.Wasm.Bootstrap.ProfiledAOTValidation
---

# Troubleshooting Profiled AOT

The .NET WebAssembly AOT compiler uses the AOT profile to determine which methods to compile to WebAssembly. In some cases, which may change depending on the .NET SDK version, selected methods may not be AOT compiled silently and may fall back to the interpreter.

In such cases, the runtime performance of the app may become slower than expected.

The bootstrapper provides a way to log the methods that were not AOT compiled, by setting the following property:

```xml
<PropertyGroup>
    <WasmShellAOTProfileValidation>true</WasmShellAOTProfileValidation>
</PropertyGroup>
```

## Assemblies filtering

Assemblies may be skipped entirely from the validation using the following property:

```xml
<PropertyGroup>
    <WasmShellAOTProfileValidationExcludedAssemblies>$(WasmShellAOTProfileValidationExcludedAssemblies);System.*</WasmShellAOTProfileValidationExcludedAssemblies>
</PropertyGroup>
```

Entries in the `WasmShellAOTProfileValidationExcludedAssemblies` property are semi-colon separated regular expressions.

## Methods filtering

Specific methods may be skipped entirely from the validation using the following property:

```xml
<PropertyGroup>
    <WasmShellAOTProfileValidationExcludedMethods>$(WasmShellAOTProfileValidationExcludedMethods);MyNamespace.MyType.MyMethod.*</WasmShellAOTProfileValidationExcludedMethods>
</PropertyGroup>
```

Entries in the `WasmShellAOTProfileValidationExcludedMethods` property are semi-colon separated regular expressions.

## Conditions when methods are not AOT compiled

### Methods with try/catch/finally blocks

Methods containing `try/catch/finally` blocks are not AOT compiled. `try/finally` and `try/catch` blocks are not impacted.

When this pattern is needed, it's best to separate place the `try/finally` and the `try/catch` in separate methods.

## Build Errors

### UNOW0001

The following error may be raised:

```text
UNOW0001: Method XXX from YYY has not been AOTed, even if present in the AOT profile.
```

This error is raised when the AOT profile requested a method to be AOT compiled, but was not.

### UNOW0002

The following error may be raised:

```text
UNOW0002: The method XXX from YYY is not present in the assembly.
```

This error generally means that there's a problem in the bootstrapper when matching methods from the compiled assemblies. If you find this specific error, please report it by opening an issue.
