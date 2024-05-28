---
uid: UnoWasmBootstrap.Features.Obfuscation
---

## Assemblies obfuscation

The Bootstrapper provides a way to obfuscate served assemblies in order to avoid incorrect flagging by anti-virus and firewalls.

### Description
Assemblies currently a critical part of the behavior of .NET, [even when using AOT](runtime-execution-modes.md). Original assemblies are still required for metadata (Reflection) and non-AOTable pieces of code (as of .NET 7, part of try/catch/finally blocks).

The Uno boostrapper packages .NET assemblies as part of the final generated web content. Those files are fetched the application directly and the default extension used by the bootstrapper for these files are `.clr` as a minimal attempt to avoid blocking.

### Changing the assemblies extensions
This feature allows to change the `.clr` extension for another extension. Add the following to your `csproj` file:

```xml
<PropertyGroup Condition="'$(Configuration)'=='Release'">
	<WasmShellAssembliesExtension>.custom</WasmShellAssembliesExtension>
</PropertyGroup>
```

> [!NOTE]
> Changing this extension requires the adjustment of `web.config` or `staticwebapp.config.json` to provide the proper `application/octet-stream` mime type.

> [!NOTE]
> As of Bootstrapper 7.0, changing the extension does not permit local Visual Studio debugging and serving. Using [dotnet-serve](https://github.com/natemcmaster/dotnet-serve) is required.

### Extension-less mode for assemblies
This feature allows to remove the extension from the assemblies URI, by removing all `.` from the assembly file names.

Add the following to your `csproj` file:
```xml
<PropertyGroup Condition="'$(Configuration)'=='Release'">
	<WasmShellAssembliesFileNameObfuscationMode>NoDots</WasmShellAssembliesFileNameObfuscationMode>
</PropertyGroup>
```

Note that serving extension-less files requires a special fallback in servers:
- For IIS:
	```xml
	<staticContent>
		...
		<mimeMap fileExtension="." mimeType="application/octet-stream" />
	</staticContent>
	```

- For ASP.NET Core, using a [`StaticFileOptions`](https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.builder.staticfileoptions?view=aspnetcore-7.0)

	```csharp
	app.UseStaticFiles(new StaticFileOptions
	{
		...
		DefaultContentType = MediaTypeNames.Application.Octet,
		ServeUnknownFileTypes = true
	});
	```

### Obfuscating the contents of assemblies
This feature allows to change the contents of the files using a simple XOR transformation applied to the generated site.

> [!WARNING]
> Ensure that enabling this feature is conforming to the requirements of the deployment environment, commonly defined by an IT department. 

To use this feature, add the following to your `csproj` file:

```xml
<PropertyGroup Condition="'$(Configuration)'=='Release'">
	<WasmShellObfuscateAssemblies>true</WasmShellObfuscateAssemblies>
</PropertyGroup>
```

> [!WARNING]
> Enabling this feature has an impact on the startup time of the application, as additional process is required to de-obfuscate the downloaded assemblies.
