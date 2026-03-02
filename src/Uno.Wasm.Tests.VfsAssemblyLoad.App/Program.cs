using System;
using System.Runtime.InteropServices.JavaScript;
using Uno.Wasm.Tests.VfsAssemblyLoad.Library;

namespace Uno.Wasm.Tests.VfsAssemblyLoad.App;

public static partial class Program
{
    static void Main()
    {
        // Call into the non-BCL library to prove it was loaded via VFS.
        var greeting = VfsTestHelper.Greet("VFS");
        Console.WriteLine(greeting);

        // Create a #results element and write to it so the Puppeteer test can verify.
        // The bootstrap-generated index.html does not include this element.
        WebAssembly.Runtime.InvokeJS(
            $"(function(){{ var el = document.createElement('div'); el.id = 'results'; el.textContent = '{greeting}'; document.body.appendChild(el); }})()");

        Console.WriteLine("VFS assembly load test completed successfully.");
    }
}

/// <summary>
/// JSExport method exercising mono_wasm_bind_assembly_exports.
/// If the main assembly or System.Runtime.InteropServices.JavaScript
/// were incorrectly redirected to VFS, binding this export would fail
/// with an ExitStatus assertion in corebindings.c.
/// </summary>
public static partial class Exports
{
    [JSExport]
    public static string GetResult()
        => VfsTestHelper.Greet("JSExport");
}
