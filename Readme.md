# Uno.Wasm.Bootstrap

[![Open in Gitpod](https://gitpod.io/button/open-in-gitpod.svg)](https://gitpod.io/#https://github.com/unoplatform/Uno.Wasm.Bootstrap)

Uno.Wasm.Bootstrap provides a simple way to package C# .NET code, and run it from a compatible browser environment.

It is a standalone .NET Web Assembly (Wasm) sdk bootstrapper taking the form of a nuget package.

Installing it on a .NET project (5, 6, 7 or .NET Standard 2.0) with an entry point allows to publish it as part of a Wasm distribution folder, along with CSS, Javascript and content files.

This package only provides the bootstrapping features to run a .NET assembly and write to the javascript console, through `Console.WriteLine`.

This package is based on the excellent work from @praeclarum's [OOui Wasm MSBuild task](https://github.com/praeclarum/Ooui).

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
  - [Pre-compression](doc/features-pre-compression.md)
  - [Embedded mode](doc/features-embedded.mode.md)
  - [Native Linker Optimization](doc/features-linker-opts.md)
  - [Memory troubleshooting](doc/features-memory-corruption-troubleshooting.md)
  - [Module Linking](doc/features-module-linking.md)
  - [Profiling](doc/features-profiling.md)
  - [Node JS](doc/features-node-js.md)
  - [Nuget package overrides](doc/features-nuget-package-overrides.md)
  - [Prefetching](doc/features-prefetch.md)
  - [PWA Support](doc/features-pwa.md)
  - [4GB Support](doc/features-4gb.md)
  - [HttpRequestMessage Extensions](doc/features-httprequestmessage-extensions.md)
  - [Assemblies obfuscation](doc/features-obfuscation.md)
- Tools
  - [Uno Version Checker](doc/features-version-checker.md)
- [Contributing](doc/contributing.md)
- [Release procedure](doc/release-procedure.md)
