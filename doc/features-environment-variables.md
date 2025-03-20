---
uid: UnoWasmBootstrap.Features.EnvironmentVariables
---

# Environment variables

Mono provides the ability to configure some features at initialization, such as logging or GC.

To set those variables, add the following to your project file:

```xml
<ItemGroup>
  <WasmShellMonoEnvironment Include="MONO_GC_PARAMS" Value="soft-heap-limit=512m,nursery-size=64m,evacuation-threshold=66,major=marksweep" />
  <WasmShellMonoEnvironment Include="MONO_LOG_LEVEL" Value="debug" />
  <WasmShellMonoEnvironment Include="MONO_LOG_MASK" Value="gc" />
</ItemGroup>
```

These lines change the configuration of the GC and logging, to determine when a GC occurs. More options are available
in the `Environment Variables` section of [the mono documentation](http://docs.go-mono.com/?link=man%3amono(1)).

## Configuration Environment Variables

The bootstrapper provides a set of environment variables that reflect the configuration provided at build time:

- `UNO_BOOTSTRAP_MONO_RUNTIME_MODE`, which specifies the runtime mode configuration (see above for valid values)
- `UNO_BOOTSTRAP_LINKER_ENABLED`, which is set to `True` if the linker was enabled, otherwise `False`
- `UNO_BOOTSTRAP_DEBUGGER_ENABLED`, which is set to `True` if the debugging support was enabled, otherwise `False`
- `UNO_BOOTSTRAP_FETCH_RETRIES`, which specifies how many ties `fetch` should retry on a failed request. Defaults to `1`
- `UNO_BOOTSTRAP_MONO_RUNTIME_CONFIGURATION`, which provides the mono runtime configuration, which can be can either be `release` or `debug`.
- `UNO_BOOTSTRAP_MONO_RUNTIME_FEATURES`, which provides a list of comma separated feature enabled in the runtime (e.g. `threads`)
- `UNO_BOOTSTRAP_MONO_PROFILED_AOT`, which specifies if the package was built using a PG-AOT profile.
- `UNO_BOOTSTRAP_APP_BASE`, which specifies the location of the app content from the base. Useful to reach assets deployed using the `UnoDeploy="Package"` mode.
- `UNO_BOOTSTRAP_WEBAPP_BASE_PATH`, which specifies the base location of the webapp. This parameter is used in the context of deep-linking (through the `WasmShellWebAppBasePath` property). This property must contain a trailing `/` and its default is `./`.
- `UNO_BOOTSTRAP_EMSCRIPTEN_MAXIMUM_MEMORY`, which optionally specifies the maximum memory available to the WebAssembly module.
- `UNO_BOOTSTRAP_MAX_THREADS`, which provides the maximum number of threads that can be created.

Those variables can be accessed through [Environment.GetEnvironmentVariable](https://docs.microsoft.com/en-us/dotnet/api/system.environment.getenvironmentvariable).
