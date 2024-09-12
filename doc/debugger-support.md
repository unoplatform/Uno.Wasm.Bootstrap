---
uid: UnoWasmBootstrap.Features.Debugger
---

# .NET for WebAssembly Debugger Support

Debugging is support is provided by .NET, using the WasmApp host.

## AOT Debugging and mono tracing

When running with AOT or Profiled AOT, exceptions generally do not provide complete stack traces, as WebAssembly as of the MVP does not yet support stack walking.

For the time being, it's still possible to view browser stack traces in the log by enabling mono tracing.

First, you'll need to add the following class to your app:

```csharp
static class MonoInternals
{
  [DllImport("__Native")]
  internal static extern void mono_trace_enable(int enable);
  [DllImport("__Native")]
  internal static extern int mono_trace_set_options(string options);
}
```

> [!NOTE]
> In order for `__Native` to be available, you'll need to specify `<WasmShellAdditionalPInvokeLibrary Include="__Native" />` item. See [Static linking additional P/Invoke libraries](features-module-linking.md#static-linking-additional-pinvoke-libraries) for details.

Then in the `Main` of your application, add the following:

```csharp
MonoInternals.mono_trace_enable(1);
MonoInternals.mono_trace_set_options("E:all");
```

This will enable the tracing of all application exceptions (caught or not), along with the associated native host stack traces.

You can find the documentation for [`mono_trace_set_options` parameter here](https://www.mono-project.com/docs/debug+profile/debug/#tracing-program-execution).
