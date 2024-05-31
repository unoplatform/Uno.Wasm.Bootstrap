---
uid: UnoWasmBootstrap.Features.LinkerConfig
---


## Linker configuration
The .NET tooling uses the [ILLinker](https://github.com/mono/linker/tree/master/), and can be configured using a linker directives file.

The Bootstrapper searches for a file placed in an ItemGroup named `LinkerDescriptor`. See examples below.

### Configuration file (commonly named `LinkerConfig.xml`)

```xml
<linker>
    <assembly fullname="Uno.Wasm.Sample"> <!-- Replace names to reflect your needs -->
        <namespace fullname="Uno.Wasm.Sample" />
    </assembly>

    <assembly fullname="WebAssembly.Bindings" />
</linker>
```

The documentation for this file [can be found here](https://github.com/dotnet/runtime/blob/9fca0c3dbd3874ed0245b1bdb10547d0ba769d66/docs/tools/illink/data-formats.md).

### Reference in project file

It is also possible to provide the linker file as an embedded resource, which is useful when creating libraries. The linker step will discover those files and apply the configuration.

``` xml
<!-- For application projects, include this in the .csproj file of your Wasm project -->
<ItemGroup>
    <LinkerDescriptor Include="LinkerConfig.xml" />
</ItemGroup>

<!-- For libraries, you should use this syntax instead -->
<ItemGroup Condition="'$(TargetFramework)' == 'net5.0'">
	<EmbeddedResource Include="LinkerConfig.xml">
		<LogicalName>$(AssemblyName).xml</LogicalName>
	</EmbeddedResource>
</ItemGroup>
```

The Linker can be disabled completely by setting the `WasmShellILLinkerEnabled` property to false. This property has no effect when building with AOT enabled.

### .NET 5 Feature Linker Configuration
The bootstrapper supports the [feature switches configuration](https://github.com/dotnet/runtime/blob/master/docs/workflow/trimming/feature-switches.md) provided by .NET 5.

By default, some features are linked out as those are not likely to be used in a WebAssembly context:
- `EventSourceSupport`
- `EnableUnsafeUTF7Encoding`
- `HttpActivityPropagationSupport`
- `InvariantGlobalization`

If you need to enable any of those features, you can set the following in your csproj first `PropertyGroup`:
```xml
<EventSourceSupport>true</EventSourceSupport>
```

Setting `InvariantGlobalization` to true will remove all satellite assemblies from the final package. 

### LinkerConfig.xml on WebAssembly
When using the Uno Platform [solution template](https://aka.platform.uno/app-wizard), a file named 'LinkerConfig.xml' is available in the folder `Platforms\WebAssembly\LinkerConfig.xml'.
