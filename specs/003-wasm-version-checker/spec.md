# Feature Specification: Uno WebAssembly Version Checker Tool

**Feature Branch**: `dev/cdb/repl`
**Created**: 2026-04-24
**Status**: Implemented
**Input**: Reverse specification for the existing `Uno.Wasm.VersionChecker` tool after the Repl-based architecture and modern bootstrapper support were added.

## Overview

The Uno WebAssembly version checker is a .NET tool that inspects a deployed Uno Platform WebAssembly application and reports the versions of the deployed app, Uno.UI assembly, .NET runtime assembly, target frameworks, build configuration, and related boot settings.

The tool is intentionally read-only. It starts from a public HTTP(S) target, discovers the Uno and .NET bootstrapper artifacts, downloads managed assemblies within the inspected origin, extracts assembly metadata, and returns rich Repl-renderable objects rather than writing directly to the console.

The primary command is:

```shell
uno-wasm-version inspect https://myapp.example.com
```

The shorthand form is also supported:

```shell
uno-wasm-version https://myapp.example.com
```

Because the package is a .NET tool, users may also run it without installing it permanently when supported by their .NET SDK tooling, for example with `dnx` / `dotnet dnx`.

## Architecture

### Host and command surface

- `src/Uno.Wasm.VersionChecker` is the Repl host and targets `net10.0`.
- `VersionCheckerReplHost.CreateApp` wires dependency injection, Repl CLI/interactive profiles, route validation, and the `inspect {target:site}` command.
- The default route also accepts `{target:site}` so a URL or host can be entered directly.
- Repl route constraints perform syntax-only target parsing. Full public-network validation is performed by the command handler immediately before inspection.
- The host returns a tuple of rich objects:
  - `VersionCheckInspectionView`
  - `VersionCheckSummaryView`
  - `ImmutableArray<VersionCheckAssemblyRow>`
  - a Repl success/error/cancelled result
- The host does not write directly to `Console.Out` or `Console.Error`; rendering is delegated to Repl.

### Core inspection service

- `src/Uno.Wasm.VersionChecker.Core` contains the reusable inspection model and service.
- `VersionCheckTarget` parses and normalizes user input into a trailing-slash HTTP(S) site URI.
- `VersionCheckService.InspectAsync` performs the inspection and returns a `VersionCheckReport`.
- `VersionCheckHttp.CreateDefaultHttpClient` owns the default HTTP behavior, including decompression, timeout, no automatic redirects, response-size limits, and connection-time public-address validation.

### Bootstrapper discovery pipeline

The service resolves the app boot metadata in this order:

1. Load the target HTML document.
2. Prefer script references to `uno-config.js`.
3. Rewrite script references to `uno-bootstrap.js` to the sibling `uno-config.js`.
4. If no script reference is found, probe `embedded.js` and resolve `const package = "package_..."` to `package_.../uno-config.js`.
5. Parse supported `uno-config.js` fields:
   - `config.uno_app_base`
   - `config.uno_remote_managedpath`
   - `config.uno_main`
   - `config.assemblies_with_size`
   - `config.dotnet_js_filename`
6. Prefer the configured `dotnet_js_filename` under the managed path.
7. Fall back to `dotnet.js`.
8. Fall back to `_framework/blazor.boot.json`, then `blazor.boot.json`.

The service supports boot config JSON embedded in `dotnet*.js` between `/*json-start*/` and `/*json-end*/`, plus standalone `blazor.boot.json` files, including UTF-8 BOM-prefixed JSON.

### Assembly metadata extraction

- Assemblies listed by `uno-config.js` take precedence when available.
- Otherwise, assemblies are read from the parsed .NET boot config resources.
- Resource shapes supported by the boot config parser:
  - object entries such as `"assembly": { "MyApp.dll": "hash" }`
  - array entries such as `"assembly": [{ "name": "MyApp.dll" }]`
  - `coreAssembly` and `assembly`
- Downloaded assemblies are parsed as PE (`MZ`) or WebCIL/WASM (`\0asm`) payloads.
- Metadata is extracted using Mono.Cecil:
  - assembly name
  - assembly version / informational version
  - file version
  - build configuration
  - target framework
  - 40-character commit SHA when present in informational version
- The runtime assembly is identified from `System.Private.CoreLib`, `mscorlib`, or `netstandard`.

## User Scenarios & Testing

### P1: Inspect a deployed Uno WebAssembly app

**User Journey**: A developer receives a deployed Uno WebAssembly site URL and wants to know which app, Uno.UI, and .NET runtime versions are currently deployed.

**Independent Test Approach**: Use a deterministic in-memory HTTP handler that serves a representative bootstrapper layout and real test assemblies.

**Acceptance Scenarios**:

```gherkin
Given a public HTTPS Uno WebAssembly app target
When the user runs "uno-wasm-version inspect <target>"
Then the tool returns an inspection object with the target, boot config source, and assembly count
And the tool returns a summary object with main assembly, runtime, Uno.UI, linker, globalization, and debug-level fields when available
And the tool returns assembly rows for every successfully parsed managed assembly
```

### P2: Support modern bootstrapper layouts

**User Journey**: A developer inspects a recently published Uno WebAssembly app whose HTML references `uno-bootstrap.js` and whose boot JSON is embedded in a hashed `dotnet.<hash>.js`.

**Independent Test Approach**: Use the modern bootstrapper regression test in `Given_VersionCheckService` with `uno-bootstrap.js`, `uno-config.js`, `dotnet_js_filename`, PE assemblies, and WebCIL assemblies.

**Acceptance Scenarios**:

```gherkin
Given a target page references package_x/uno-bootstrap.js
And package_x/uno-config.js declares uno_app_base, uno_remote_managedpath, and dotnet_js_filename
And dotnet.<hash>.js contains embedded boot JSON
When the tool inspects the site
Then the report resolves package_x/uno-config.js
And the report resolves package_x/_framework/dotnet.<hash>.js
And the report has non-null main assembly and runtime version fields
```

### P3: Run safely against remote input

**User Journey**: A developer runs the tool against arbitrary public URLs during investigation and expects it not to pivot into private infrastructure or exhaust local resources.

**Independent Test Approach**: Unit tests validate target parsing, same-origin path resolution, private-address rejection, response size limits, cancellation, and retry behavior.

**Acceptance Scenarios**:

```gherkin
Given a target resolves to loopback, link-local, private, carrier-grade NAT, multicast, or IPv6 unique-local addresses
When the user enters the target
Then the target is rejected before inspection

Given a remote config field contains an absolute URL for another origin
When the service resolves that field
Then inspection fails with a same-origin validation error
```

## Requirements

### Functional Requirements

- **FR-001**: The tool MUST expose an `inspect {target:site}` command and a shorthand `{target:site}` route.
- **FR-002**: Repl route constraints MUST use syntax-only target parsing and MUST NOT perform DNS resolution or public/private network validation during command parsing.
- **FR-003**: The target parser MUST accept absolute `http://` and `https://` URLs, and hostnames without a scheme by assuming `https://`.
- **FR-004**: Full target validation MUST reject unsupported schemes, malformed URLs, empty input, and targets that resolve to local or private network addresses before inspection starts.
- **FR-005**: The target parser MUST strip user info from normalized target URIs and from displayed error messages.
- **FR-006**: The default HTTP client MUST disable automatic redirects.
- **FR-007**: The default HTTP client MUST enable automatic decompression for supported encodings.
- **FR-008**: The default HTTP client MUST enforce a 30-second timeout, a 10-second connect timeout, a 64 MiB response limit, and a 5-minute pooled connection lifetime.
- **FR-009**: The default HTTP client MUST re-check resolved connection endpoints and connect only to public IP addresses.
- **FR-010**: The service MUST only resolve remote-config-derived paths within the inspected site's scheme and authority.
- **FR-011**: The service MUST only consider HTML script references that resolve within the inspected site's scheme and authority. Cross-origin absolute script URLs and network-path URLs such as `//host/uno-config.js` MUST NOT be fetched.
- **FR-012**: The service MUST discover `uno-config.js` from trusted direct script references, `uno-bootstrap.js` sibling rewriting, or `embedded.js` package declarations.
- **FR-013**: The service MUST parse supported `uno-config.js` fields and tolerate malformed JSON values in individual assignments by ignoring the malformed field.
- **FR-014**: The service MUST prefer a configured `dotnet_js_filename`, then `dotnet.js`, then standalone boot JSON.
- **FR-015**: The service MUST parse embedded boot JSON from `dotnet*.js` files using `/*json-start*/` and `/*json-end*/` markers.
- **FR-016**: The service MUST parse standalone `blazor.boot.json` from `_framework/` before the site root.
- **FR-017**: The service MUST tolerate leading whitespace and a UTF-8 BOM when detecting standalone boot JSON.
- **FR-018**: The service MUST support .NET boot config resource entries represented as either objects or arrays with `name` fields.
- **FR-019**: The service MUST parse both PE assemblies and WebCIL/WASM assemblies.
- **FR-020**: The service MUST return an immutable `VersionCheckReport` containing inspection metadata, summary fields, and assembly metadata.
- **FR-021**: The Repl host MUST return rich renderable objects instead of manually writing report content to the console.
- **FR-022**: The Repl host MUST propagate cancellation and return exit code 130 for user-initiated cancellation.
- **FR-023**: The service MUST retry HTTP 429 responses up to two times and respect `Retry-After` when present.
- **FR-024**: The service MUST cap concurrent assembly downloads at eight.
- **FR-025**: The service MUST trace assembly parsing failures and continue with other assemblies.
- **FR-026**: Inspection MUST fail with a clear error when no assemblies can be found from either Uno config or .NET boot config.

### Key Entities

- **VersionCheckTarget**: Original input plus normalized site URI. It is the validated boundary between user input and inspection.
- **VersionCheckReport**: Full inspection result, including target, server, config URLs, boot source, main assembly, Uno.UI assembly, runtime details, and all assembly rows.
- **AssemblyVersionInfo**: Metadata extracted from one assembly payload.
- **UnoConfig**: Parsed Uno bootstrapper configuration, including managed path, main assembly, assembly list, server, and configured .NET script filename.
- **DotnetConfig**: Parsed .NET boot configuration, including main assembly, globalization mode, resources, debug level, linker flag, and source URL.

## Security and Resource Boundaries

- Targets must be public HTTP(S) endpoints; local and private networks are out of scope for the default tool.
- Automatic redirects are disabled so a public host cannot redirect inspection to a private endpoint.
- DNS is validated at parse time when possible and again during connection establishment to reduce DNS-rebinding risk.
- Absolute URLs supplied by remote HTML/config data are not trusted; resolved URIs must remain same-origin with the inspected target.
- Response bodies are read through a bounded stream with a 64 MiB cap to reduce decompression-bomb and memory-exhaustion risk.
- Regex operations use compiled, culture-invariant expressions with a two-second match timeout.
- Cancellation must interrupt long-running network or assembly download operations.

## Success Criteria

- **SC-001**: The unit test suite for `Uno.Wasm.VersionChecker.UnitTests` passes with all bootstrapper discovery, parsing, security, Repl host, and regression scenarios.
- **SC-002**: A modern package-based bootstrapper layout yields a complete report with non-null main assembly and runtime version fields.
- **SC-003**: Private, loopback, link-local, multicast, carrier-grade NAT, IPv6 site-local, and IPv6 unique-local targets are rejected.
- **SC-004**: Cross-origin pivots through `uno_app_base`, `uno_remote_managedpath`, `dotnet_js_filename`, or assembly resource paths are rejected.
- **SC-005**: A compressed boot script succeeds when using the default HTTP client with automatic decompression.
- **SC-006**: PE and WebCIL payloads both produce assembly metadata rows.
- **SC-007**: Repl output is represented as structured objects and can be rendered by Repl without direct console output from the host.
- **SC-008**: Cancellation returns a cancelled Repl result rather than a partial success or generic exception.
- **SC-009**: CI builds the `Uno.Wasm.VersionChecker.UnitTests` Microsoft Testing Platform executable and publishes its TRX results.

## Edge Cases

- **Host-only input**: `myapp.example.com` is normalized to `https://myapp.example.com/`.
- **Missing trailing slash**: accepted targets are normalized to a trailing-slash site URI so relative paths resolve from the site root.
- **User info in URL**: credentials are stripped before inspection and display.
- **`uno-bootstrap.js` script tag**: resolved by replacing the filename with `uno-config.js`.
- **No Uno config**: the service falls back to .NET boot config discovery.
- **Malformed `uno-config.js` assignment**: ignored at field level; other parseable fields continue to apply.
- **Duplicate `uno-config.js` fields**: later parseable assignments override earlier values until all tracked fields are present.
- **Missing or unparsable assemblies**: unparsable assemblies are skipped, but inspection succeeds when at least one assembly is parsed.
- **429 responses**: retried with standards-aware delay handling.
- **Compressed content**: supported through the default HTTP client's decompression behavior; callers that inject a custom `HttpClient` own equivalent configuration.

## Non-Goals

- Inspecting local development servers, private intranet hosts, loopback addresses, or cloud metadata endpoints by default.
- Following redirects.
- Writing report tables manually to the console.
- Mutating, downloading, patching, or publishing the inspected application.
- Guaranteeing that every assembly listed by the boot config can be parsed; best-effort parsing is sufficient as long as the report contains the successfully parsed assemblies.
- Supporting pre-Repl legacy console output behavior.

## Related Implementation Files

- `src/Uno.Wasm.VersionChecker/Program.cs`
- `src/Uno.Wasm.VersionChecker/VersionCheckerReplHost.cs`
- `src/Uno.Wasm.VersionChecker/VersionCheckReplViews.cs`
- `src/Uno.Wasm.VersionChecker.Core/VersionCheckService.cs`
- `src/Uno.Wasm.VersionChecker.Core/VersionCheckTarget.cs`
- `src/Uno.Wasm.VersionChecker.Core/VersionCheckHttp.cs`
- `src/Uno.Wasm.VersionChecker.Core/VersionCheckNetworkPolicy.cs`
- `src/Uno.Wasm.VersionChecker.Core/VersionCheckModels.cs`
- `src/Uno.Wasm.VersionChecker.UnitTests/Given_VersionCheckService.cs`
- `src/Uno.Wasm.VersionChecker.UnitTests/Given_ReplHost.cs`
- `src/Uno.Wasm.VersionChecker.UnitTests/Given_VersionCheckTarget.cs`
