---
uid: Uno.Wasm.Bootstrap.JSInterop
---

# Interoperating with Javascript

The Uno bootstrapper provides the ability to interoperate from and to Javascript from .NET.

Two techniques are available:

- The use of .NET 7's newly generated interop features. It uses code generation to create performant, [CSP-Compliant](xref:Uno.Wasm.Bootstrap.Features.Security), thread-safe interop and does not make of the unsafe javascript `eval`.
- The use of the legacy C# `Interop.Runtime.InvokeJS(...)` and Javascript `mono_bind_static_method(...)`.

## Invoking C# code from Javascript

.NET 7 introduces the [`[JSExportAttribute]`](https://learn.microsoft.com/en-us/dotnet/api/system.runtime.interopservices.javascript.jsexportattribute?view=net-7.0) which allows for Javascript to invoke C# static methods in a memory, threading and performance efficient way.

> [!IMPORTANT]
> To enable this feature, you'll need to use `net7.0` or later in your project's `TargetFramework`, and you will need to use the Uno Bootstrapper 7.x or later.

In your C# project (named `MyApp` for this example), add the following class:

```csharp
namespace MyNamespace;

public static partial class Exports
{
    [System.Runtime.InteropServices.JavaScript.JSExport()]
    public static string MyExportedMethod()
    {
        return $"Invoked";
    }
}
```

Then in your Javascript, add the following:

```js
async function invokeCSMethod() {
    globalThis.myExports = await Module.getAssemblyExports("MyApp");
    var result = globalThis.myExports.MyNamespace.Exports.MyExportedMethod();
}

invokeCSMethod();
```

<!-- markdownlint-disable MD020 MD003 -->
## Invoking Javascript code from C#

Invoking JS functions from C# can be done through [`[JSImportAttribute]`](https://learn.microsoft.com/en-us/dotnet/api/system.runtime.interopservices.javascript.jsimportattribute?view=net-7.0).

> [!IMPORTANT]
> To enable this feature, you'll need to use `net7.0` or later in your project's `TargetFramework`.

In your C# project (named `MyApp` for this example), add the following class:

```csharp
namespace MyNamespace;

public static partial class Imports
{
    [System.Runtime.InteropServices.JavaScript.JSImport("globalThis.myJSMethod")]
    public static partial string MyJSMethod();
}
```

Then in your Javascript, add the following:

```js
function myJSMethod(){
    return "myJSMethod invoked!";
}
```

In the C# code, call `Imports.MyJSMethod();` as you would any other method.
