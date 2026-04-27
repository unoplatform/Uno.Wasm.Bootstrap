---
uid: UnoWasmBootstrap.Features.VersionChecker
---

# Uno.Wasm.VersionChecker

A Repl-based command-line tool that inspects a deployed Uno Platform WebAssembly application and extracts version information from its .NET assemblies. It works with applications built on the Uno Bootstrapper, including modern .NET 8+ deployments.

## What it detects

- Assembly names, versions, file versions, build configuration, target framework, and git commit hashes
- The main application assembly and its configuration (Release/Debug)
- Uno.UI version (if present)
- .NET runtime version
- Globalization mode, linker status, and debug level (from the boot configuration)

## Supported application formats

The tool automatically detects the deployment format and uses the appropriate extraction strategy:

| Format | .NET Version | How it works |
|---|---|---|
| Embedded boot config in `dotnet.js` | .NET 8+ | Extracts JSON between `/*json-start*/` and `/*json-end*/` markers |
| `blazor.boot.json` | .NET 6-7 | Reads the standalone JSON configuration file |
| `uno-config.js` with `assemblies_with_size` | Legacy | Parses the JavaScript configuration directly |

Assembly files are supported in both PE (`.dll`) and WebCIL (`.wasm`) formats.

## Quick start with `dnx`

With .NET 10+, the recommended way to try the tool is to run it directly with [`dnx`](https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-10/sdk), without installing it first:

```shell
dnx Uno.Wasm.VersionChecker myapp.example.com
```

If you prefer the explicit form, this works too:

```shell
dotnet dnx Uno.Wasm.VersionChecker myapp.example.com
```

Both commands download and run the latest version on the fly.

## Installation (optional)

If you prefer a global installation for repeated use, you can still install the tool:

```shell
dotnet tool install -g Uno.Wasm.VersionChecker
```

To update to the latest version:

```shell
dotnet tool update -g Uno.Wasm.VersionChecker
```

## Usage

Pass the URL of your deployed Uno application:

```shell
uno-wasm-version https://myapp.example.com
```

The `https://` scheme is assumed if not specified, so this also works:

```shell
uno-wasm-version myapp.example.com
```

The tool runs on .NET 10 and uses Repl as its single command host. Running it without a target starts the interactive prompt, and you can use either the direct shorthand or an explicit command:

```shell
uno-wasm-version
> inspect https://myapp.example.com
```

```shell
uno-wasm-version
> myapp.example.com
```

## Example output

In human mode, Repl renders the inspection as structured sections and tables:

```console
Field            Value
Tool             uno-wasm-version
ToolVersion      3.x.x
Target           https://myapp.example.com/
UnoConfigUrl     https://myapp.example.com/package_abc123/uno-config.js
BootConfigSource dotnet.7kx2mq.js
MainAssembly     MyApp
AssemblyCount    42

Field               Value
MainAssembly        MyApp
MainAssemblyVersion 1.2.0
MainAssemblyBuild   Release
UnoUiVersion        5.6.100
RuntimeFramework    .NETCoreApp,Version=v10.0
RuntimeVersion      10.0.0
GlobalizationMode   hybrid
Linker              enabled
DebugLevel          0

Name                   Version FileVersion      Build   Framework                 Commit
MyApp                  1.2.0   1.2.0.0          Release .NETCoreApp,Version=v10.0 a1b2c3d4...
System.Private.CoreLib 10.0.0  10.0.025.7105   Release .NETCoreApp,Version=v10.0
Uno.UI                 5.6.100 255.255.255.255 Release .NETStandard,Version=v2.0 e5f6a7b8...

Success: Inspection completed. Found 42 assemblies.
```

## Exit codes

| Code | Meaning |
|---|---|
| `0` | Success |
| `1` | No assemblies found or invalid configuration |
| `250` | Invalid URL |
| `255` | Unexpected error |
