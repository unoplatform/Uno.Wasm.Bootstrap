# Feature Specification: WebAssembly Tools Workload Diagnostic (UNOWA0001)

**Feature Branch**: `dev/ap/unowa0001-tfm-workload`
**Created**: 2026-11-12
**Status**: Implemented
**Input**: Bug fix so that UNOWA0001 names the workload that is missing for the project's target framework (unoplatform/Uno.Wasm.Bootstrap#1136).

## Overview

`_UnoValidateWasmToolsWorkload` reports UNOWA0001 when a project needs native WebAssembly compilation, but the .NET for WebAssembly toolchain is not available. The project needs it when it sets `WasmBuildNative`, or has `NativeFileReference`, `WasmShellNativeCompile`, `WasmShellAdditionalPInvokeLibrary`, `WasmShellExtraEmccFlags`, `EmccExportedRuntimeMethod` or native assets. The toolchain is available when the SDK's mono toolchain targets set `WasmNativeWorkloadAvailable` to `true`.

The SDK provides that toolchain per target framework. On the .NET 11 SDK, `wasm-tools` covers `net11.0` only (`WasmNativeWorkload11`), and a `net10.0` project needs `wasm-tools-net10` (`WasmNativeWorkload10`). The error used to name `wasm-tools` in all cases. For a `net10.0-browserwasm` app built with the .NET 11 SDK, it told the developer to install a workload they already had.

This specification makes the error name the workload for the project's target framework, along with the target framework and SDK version it checked.

## Functional Requirements

**FR-1**: The detection of when the toolchain is required, and of whether it is available, SHALL be unchanged. The error SHALL still be raised only when `_UnoRequiresWasmTools` is `true` and `WasmNativeWorkloadAvailable` is not `true`.

**FR-2**: The suggested workload SHALL be `wasm-tools` when the SDK builds the project with its current toolchain (`TargetsCurrent` is `true`), or when the project's `TargetFrameworkVersion` is not older than the SDK's `BundledNETCoreAppTargetFrameworkVersion`.

**FR-3**: Otherwise, the suggested workload SHALL be `wasm-tools-net<N>`, where `<N>` is the major version of the project's target framework. For example, it is `wasm-tools-net10` for `net10.0-browserwasm` on the .NET 11 SDK.

**FR-4**: The message SHALL include the project's `TargetFramework`, the SDK version (`NETCoreSdkVersion`), and the suggested `dotnet workload install` command. It SHALL keep the uno.check link.

**FR-5**: `UnoSkipWasmToolsWorkloadValidation=true` SHALL keep disabling the check.

## Non-Goals

- Changing when native compilation is considered required, or how `WasmNativeWorkloadAvailable` is computed (the SDK owns it).
- Installing workloads or checking workload versions.
- Diagnosing a target framework newer than the SDK, which the SDK rejects earlier (NETSDK1045).

## Edge Cases

- The SDK's mono toolchain targets contain a disabled `TreatPreviousAsCurrent` switch that would build the previous target framework with the current toolchain. FR-2 checks `TargetsCurrent` so that the message keeps suggesting `wasm-tools` if that switch is enabled.
- If `TargetFrameworkVersion` or `BundledNETCoreAppTargetFrameworkVersion` is empty, the version comparison is skipped and `wasm-tools` is suggested, so the check itself cannot fail the build.
- Target frameworks older than `net9.0` are already rejected by `_UnoWasmValidateCompatibleSdk`, so in practice `<N>` is the previous one or two majors.

## Validation

- On the .NET 10.0.400 SDK, the property logic yields `wasm-tools` for `net10.0`, `wasm-tools-net9` for `net9.0` and `wasm-tools-net8` for `net8.0`.
- Building a `net10.0-browserwasm` app that uses `WasmShellNativeCompile`, with the .NET 11 SDK, `wasm-tools` installed and `wasm-tools-net10` not installed, reports UNOWA0001 with `net10.0-browserwasm`, the SDK version and `dotnet workload install wasm-tools-net10`. After installing `wasm-tools-net10`, the build succeeds.
- Building a `net11.0-browserwasm` app with the .NET 11 SDK and without `wasm-tools` reports UNOWA0001 with `dotnet workload install wasm-tools`.
