# Uno.Wasm.Bootstrap

[![Open in Gitpod](https://gitpod.io/button/open-in-gitpod.svg)](https://gitpod.io/#https://github.com/unoplatform/Uno.Wasm.Bootstrap)

The Uno.Wasm.Bootstrap package provides a runtime bootstrapper of the `Microsoft.NET.Sdk.WebAssembly` SDK from .NET 9.

This package only provides the bootstrapping features to run a .NET assembly and write to the JavaScript console, through `Console.WriteLine`. To write an app that provides UI functionalities, make sur to check out https://aka.platform.uno/get-started.

This work is based on the excellent work from @praeclarum's [OOui Wasm MSBuild task](https://github.com/praeclarum/Ooui).

## Documentation

- [Using the bootstrapper](doc/using-the-bootstrapper.md)
- [Debugger support](doc/debugger-support.md)
- [Deploy and publish](doc/deploy-and-publish.md)
- [Linker configuration](doc/linker-configuration.md)
- [Runtime Execution Modes](doc/runtime-execution-modes.md)
- [Troubleshooting](doc/troubleshooting.md)
- Features
  - [Using additional files](doc/features-additional-files.md)
  - [Interoperating with Javascript](doc/features-interop.md)
  - [Javascript Dependency management](doc/features-dependency-management.md)
  - [Environment Variables](doc/features-environment-variables.md)
  - [Splash screen](doc/features-splash-screen.md)
  - [Threading](doc/features-threading.md)
  - [Deep linking](doc/features-deep-linking.md)
  - [Embedded mode](doc/features-embedded.mode.md)
  - [Native Linker Optimization](doc/features-linker-opts.md)
  - [Memory troubleshooting](doc/features-memory-corruption-troubleshooting.md)
  - [Module Linking](doc/features-module-linking.md)
  - [Profiling](doc/features-profiling.md)
  - [PWA Support](doc/features-pwa.md)
  - [4GB Support](doc/features-4gb.md)
  - [HttpRequestMessage Extensions](doc/features-httprequestmessage-extensions.md)
  - [dotnet.js Fingerprinting](doc/features-dotnetjs-fingerprinting.md)
  - [Assemblies obfuscation](doc/features-obfuscation.md)
- Tools
  - [Uno Version Checker](doc/features-version-checker.md)
- [Contributing](doc/contributing.md)
- [Release procedure](doc/release-procedure.md)

## Previous releases documentation

- [8.0.x](https://github.com/unoplatform/Uno.Wasm.Bootstrap/tree/release/stable/8.0/doc)
- [7.0.x](https://github.com/unoplatform/Uno.Wasm.Bootstrap/tree/release/stable/7.0/doc)
- [3.x](https://github.com/unoplatform/Uno.Wasm.Bootstrap/tree/release/stable/3.3/doc)
