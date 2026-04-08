using System;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;

namespace Uno.Wasm.Tests.WorkerFork.App;

[SupportedOSPlatform("browser")]
public static partial class Program
{
    private static readonly string[] WorkerArgs = ["--worker"];
    static void Main(string[] args)
    {
        var isWorker = Array.Exists(args, a => a == "--worker")
            || Environment.GetEnvironmentVariable("UNO_BOOTSTRAP_IS_WORKER") == "true";

        if (isWorker)
        {
            RunWorker();
        }
        else
        {
            RunMain();
        }
    }

    static void RunMain()
    {
        Console.WriteLine("Main thread: forking to worker...");

        try
        {
            // Register callbacks first, then fork.
            MainInterop.SetOnMessageCallback(OnWorkerMessage);
            MainInterop.SetOnErrorCallback(OnWorkerError);
            MainInterop.Fork(WorkerArgs);

            Console.WriteLine("Main thread: fork initiated.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Main thread: error during fork: {ex.Message}");
        }
    }

    [JSExport]
    public static void OnWorkerMessage(string json)
    {
        Console.WriteLine($"Main thread: received from worker: {json}");

        // Check for the ready signal.
        if (json.Contains("\"__workerReady\":true"))
        {
            Console.WriteLine("Main thread: worker is ready, sending test message");
            MainInterop.SendMessage("{\"text\":\"hello from main\"}");
            return;
        }

        // Write the result to a #results div for E2E test validation.
        MainInterop.SetResultDiv(json);
    }

    [JSExport]
    public static void OnWorkerError(string error) =>
        Console.Error.WriteLine($"Main thread: worker error: {error}");

    static void RunWorker()
    {
        Console.WriteLine("Worker: started");
        Console.WriteLine("Worker: UNO_BOOTSTRAP_IS_WORKER = " +
            Environment.GetEnvironmentVariable("UNO_BOOTSTRAP_IS_WORKER"));

        // Register the message handler.
        WorkerInterop.RegisterMessageCallback(OnMainThreadMessage);

        Console.WriteLine("Worker: message callback registered.");
    }

    [JSExport]
    public static void OnMainThreadMessage(string json)
    {
        Console.WriteLine($"Worker: received message: {json}");

        // Echo back with modifications.
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var text = doc.RootElement.TryGetProperty("text", out var t) ? t.GetString() : "";
        var response = $"{{\"text\":\"{text}\",\"echo\":true,\"workerResponse\":\"processed: {text}\"}}";
        WorkerInterop.PostMessage(response);
    }
}

/// <summary>
/// JS interop for the main thread (calls into WorkerFork bridge).
/// </summary>
internal static partial class MainInterop
{
    [JSImport("globalThis.Uno.WebAssembly.Bootstrap.WorkerFork.fork")]
    public static partial void Fork([JSMarshalAs<JSType.Array<JSType.String>>] string[] args);

    [JSImport("globalThis.Uno.WebAssembly.Bootstrap.WorkerFork.sendMessage")]
    public static partial void SendMessage(string json);

    [JSImport("globalThis.Uno.WebAssembly.Bootstrap.WorkerFork.terminateWorker")]
    public static partial void TerminateWorker();

    [JSImport("globalThis.Uno.WebAssembly.Bootstrap.WorkerFork.setOnMessageCallback")]
    public static partial void SetOnMessageCallback([JSMarshalAs<JSType.Function<JSType.String>>] Action<string> callback);

    [JSImport("globalThis.Uno.WebAssembly.Bootstrap.WorkerFork.setOnErrorCallback")]
    public static partial void SetOnErrorCallback([JSMarshalAs<JSType.Function<JSType.String>>] Action<string> callback);

    [JSImport("globalThis.__unoSetResultDiv")]
    public static partial void SetResultDiv(string json);
}

/// <summary>
/// JS interop for the worker thread.
/// </summary>
internal static partial class WorkerInterop
{
    [JSImport("globalThis.__unoWorkerPostMessage")]
    public static partial void PostMessage(string json);

    [JSImport("globalThis.__unoWorkerSetMessageCallback")]
    public static partial void RegisterMessageCallback([JSMarshalAs<JSType.Function<JSType.String>>] Action<string> callback);
}
