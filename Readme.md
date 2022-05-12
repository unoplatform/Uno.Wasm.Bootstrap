# Uno.Wasm.Bootstrap

[![Open in Gitpod]((https://gitpod.io/button/open-in-gitpod.svg)]((https://gitpod.io/#https://github.com/unoplatform/Uno.Wasm.Bootstrap) 

Uno.Wasm.Bootstrap provides a simple way to package C# .NET code, and run it from a compatible browser environment.

It is a standalone .NET Web Assembly (WASM) sdk bootstrapper taking the form of a nuget package.

Installing it on a .NET 5 project or .NET Standard 2.0 library with an entry point allows to publish it as part of a WASM distribution folder, along with CSS, Javascript and content files.

This package only provides the bootstrapping features to run a .NET assembly and write to the javascript console, through `Console.WriteLine`.

This package is based on the excellent work from @praeclarum's [OOui Wasm MSBuild task]((https://github.com/praeclarum/Ooui).

## Documentation
- [Using the bootstrapper](doc/using-the-bootstrapper.md)
- [Debugger support](debugger-support.md)
- [Deploy and publish](deploy-and-publish.md)
- [Linker configuration](linker-configuration.md)
- [Runtime Exection Modes](runtime-execution-modes.md)
- [Troubleshooting](troubleshooting.md)

- Features
    - [Using additional files](features-additional-files.md)
    - [Javascript Dependency management](features-dependency-management.md)
    - [Deep linking](features-deep-linking.md)
    - [Splash screen](features-splash-screen.md)
    - [Environment Variables](features-environment-variables.md)
    - [Pre-compression](features-pre-compression.md)
    - [Embedded mode](features-embedded.mode.md)
    - [Native Linker Optimization](features-linker-opts.md)
    - [Memory troubleshooting](features-memory-corruption-troubleshooting.md)
    - [Module Linking](features-module-linking.md)
    - [Profiling](features-profiling.md)
    - [Node JS](features-node-js.md)
    - [Nuget package overrides](features-nuget-package-overrides.md)
    - [Prefechting](features-prefetch.md)
    - [PWA Support](features-pwa.md)
    - [4GB Support](features-4gb.md)

- [Contributing](contributing.md)
