namespace Uno.Wasm.Tests.VfsAssemblyLoad.Library;

/// <summary>
/// A non-BCL assembly that must be loaded via VFS when
/// WasmShellVfsFrameworkAssemblyLoad is enabled. Calling into
/// this type from the main app proves the assembly was resolved
/// correctly through MONO_PATH probing.
/// </summary>
public static class VfsTestHelper
{
    public static string Greet(string name)
        => $"Hello from VFS-loaded library, {name}!";
}
