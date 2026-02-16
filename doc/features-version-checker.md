---
uid: UnoWasmBootstrap.Features.VersionChecker
---

# Uno.Wasm.VersionChecker

A command-line tool that inspects a deployed Uno Platform WebAssembly application and extracts version information from its .NET assemblies. It works with applications built on the Uno Bootstrapper, including modern .NET 8+ deployments.

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

## Installation

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

## Example output

```console
Uno Version Checker v3.x.x.
Checking website at address https://myapp.example.com/.
Trying to find App configuration...
Application found.
Uno configuration url is https://myapp.example.com/package_abc123/uno-config.js.
Boot configuration extracted from dotnet.7kx2mq.js.
Starting assembly is MyApp.
Trying to download 42 files to find assemblies. Downloading them to read metadata...

42 assemblies successfully downloaded.
 Name                          | Version   | File Version  | Build   | Framework                    | Commit
 MyApp                         | 1.2.0     | 1.2.0.0       | Release | .NETCoreApp,Version=v10.0    | a1b2c3d4...
 System.Private.CoreLib        | 10.0.0    | 10.0.025.7105  | Release | .NETCoreApp,Version=v10.0    |
 Uno.UI                        | 5.6.100   | 255.255.255.255| Release | .NETStandard,Version=v2.0    | e5f6a7b8...
 ...

MyApp version is 1.2.0 (Release)
Uno.UI version is 5.6.100
Runtime is .NETCoreApp,Version=v10.0 version 10.0.0
Globalization mode is hybrid
Linker is enabled
Debug level is 0
```

## Exit codes

| Code | Meaning |
|---|---|
| `0` | Success |
| `1` | No assemblies found or invalid configuration |
| `100` | No URL argument provided |
| `250` | Invalid URL |
| `255` | Unexpected error |
