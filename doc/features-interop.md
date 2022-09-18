## Interoperating with Javascript

The Uno boostrapper provides the ability to interoperate from and to Javascript from .NET.

Two techniques are available:
- The use of .NET 7's new generated interop features. It uses code generation to create performant, thread-safe and does not make of the unsafe javascript `eval`.
- The use of the legacy C# `Interop.Runtime.InvokeJS(...)` and Javascript `mono_bind_static_method(...)`.

## Invoking C# code from Javascript

# [**.NET 7 JSExport**](#tab/net7)

.NET 7 introduces the [`[JSExportAttribute]`](https://learn.microsoft.com/en-us/dotnet/api/system.runtime.interopservices.javascript.jsexportattribute?view=net-7.0) allows for Javascript to invoke C# static methods in a memory, threading and performance efficient way. 

> [!IMPORTANT]
> To enable these features, you'll need to use `net7.0` or later in your project's `TargetFramework`, and you will need to use the Uno Bootstrapper 7.x or later.

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

# [**.NET 6 `mono_bind_static_method`**](#tab/jseval)

In your C# project (named `MyApp` for this example), add the following class:
```csharp
namespace MyNamespace;

public static partial class Exports
{
    public static string MyExportedMethod()
    {
        return $"Invoked";
    }
}
```

Then in your Javascript, add the following:

```js
var myExportedMethod = Module.mono_bind_static_method("[MyApp] MyNamespace.Exports:MyExportedMethod");
var result = myExportedMethod();
```

> [!IMPORTANT]
> The interop infrastructure only supports primitive types for parameters and return value.

***

## Invoking Javascript code from C#

# [**.NET 7 JSImport**](#tab/net7)

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

# [**.NET 6 JSEval**](#tab/jseval)

If you're not using Uno.UI, you'll need to define the following in the global namespace:
```csharp
internal sealed class Interop
{
	internal sealed class Runtime
	{
		[MethodImplAttribute(MethodImplOptions.InternalCall)]
		public static extern string InvokeJS(string str, out int exceptional_result);
	}
}
```

Then in your Javascript, add the following:

```js
function myJSMethod(){
    console.log("myJSMethod invoked!");
}
```

And can be used from C# with:
```cs
InvokeJS("myJSMethod()");
```

Note that the interop only supports strings as the returned value, and parameters must formatted in the string passed to `InvokeJS`.

***
