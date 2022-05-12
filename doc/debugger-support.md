Uno.Wasm.Bootstrap provides a simple way to package C# .NET code, and run it from a compatible browser environment.

It is a standalone .NET Web Assembly (WASM) sdk bootstrapper taking the form of a nuget package.

Installing it on a .NET 5 project or .NET Standard 2.0 library with an entry point allows to publish it as part of a WASM distribution folder, along with CSS, Javascript and content files.

This package only provides the bootstrapping features to run a .NET assembly and write to the javascript console, through `Console.WriteLine`.

This package is based on the excellent work from @praeclarum's [OOui Wasm MSBuild task](https://github.com/praeclarum/Ooui).

## .NET for WebAssembly Debugger Support

Debugging is supported through the integration of a .NET Core CLI component, which acts as a static files server, as well as a debugger proxy for Chrome (other browsers are not supported).

### Enable the Debugger support

In order to debug an **Uno.Wasm.Boostrap** enabled project, the Mono runtime debugger must be enabled:

```xml
<PropertyGroup Condition="'$(Configuration)'=='Debug'">
   <MonoRuntimeDebuggerEnabled>true</MonoRuntimeDebuggerEnabled>
</PropertyGroup>
```

Debug symbols need to be emitted and be of the type `portable`:

```xml
<PropertyGroup Condition="'$(Configuration)'=='Debug'">
    <DebugType>portable</DebugType>
    <DebugSymbols>true</DebugSymbols>
</PropertyGroup>
```

Finally, the `DEBUG` constant must be defined

```xml
<PropertyGroup Condition="'$(Configuration)'=='Debug'">
    <DefineConstants>$(DefineConstants);TRACE;DEBUG</DefineConstants>
</PropertyGroup>
```

Doing so will enable the deployment of `pdb` files to the browser, and allow for the mono debugger proxy to use them. 

For the time being, you will also need to make sure that mscorlib is disabled in the Linker configuration file: 

```xml
<!-- Required for debugging -->
<assembly fullname="mscorlib" />
<assembly fullname="System.Core" />
```

.NET for WebAssembly now has integrated **preliminary** support for in-browser debugging. Refer to
[this document for up-to-date information](https://github.com/mono/mono/tree/master/sdks/wasm#debugging) on how to set up the debugging.

### How to use the Browser debugger
The boostrapper also supports debugging directly in the browser debugging tools.

In Visual Studio:
- Make your project the startup project (right-click **set as startup**)
- In the debugging toolbar:
  - Select **IIS Express** as the debugging target
  - Select **Chrome** as the Web Browser
  - Make sure script debugging is disabled
- Start the debugging session using F5 (or Start Debug)
- Once your application has started, press `Alt+Shift+D`
- Follow the instructions on the web page
- You may need to refresh the original tab if you want to debug the entry point (Main) of your application.

### Debugger troubleshooting
The debugger is still under development, and here are a few things to look for:
- Breakpoints set sometimes disappear when the debugged page is reloaded
- If none of your assemblies appear in the debugger window, it's generally caused 
by the debugger caching previously loaded files. Make sure to hit Ctrl+Shit+R to force 
reload the debugged page.

### AOT Debugging and mono tracing (.NET 5 only)

When running with PG-AOT/FullAOT, exceptions generally do not provide stack traces, as WebAssembly as of the MVP does not yet support stack walking.

For the time being, it's still possible view browser stack traces in the log by enabling mono tracing.

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

Then in the `Main` of your application, add the following:
```csharp
MonoInternals.mono_trace_enable(1);
MonoInternals.mono_trace_set_options("E:all");
```

This will enable the tracing of all application exceptions (caught or not), along with the associated native host stack traces.

You can find the documentation for [`mono_trace_set_options` parameter here](https://www.mono-project.com/docs/debug+profile/debug/#tracing-program-execution).
