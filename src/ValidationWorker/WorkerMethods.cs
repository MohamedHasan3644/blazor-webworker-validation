using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;

namespace ValidationWorker;

// Define [JSExport] methods here to run them in a Web Worker.
// Call them from your Blazor app using WebWorkerClient.InvokeAsync.
// Example: await worker.InvokeAsync<string>("ValidationWorker.WorkerMethods.Greet", ["World"]);

[SupportedOSPlatform("browser")]
public static partial class WorkerMethods
{
    [JSExport]
    public static string Fast(string operationId) =>
        $"Fast call {operationId} returned on worker thread {Environment.CurrentManagedThreadId}.";

    [JSExport]
    public static async Task<string> SlowAsync(int delayMs, string operationId)
    {
        await Task.Delay(delayMs);
        return $"Slow call {operationId} returned after {delayMs} ms on worker thread {Environment.CurrentManagedThreadId}.";
    }

    [JSExport]
    public static async Task<string> NeverAsync(string operationId)
    {
        await Task.Delay(Timeout.InfiniteTimeSpan);
        return operationId;
    }

    [JSExport]
    public static string Fail(string operationId) =>
        throw new InvalidOperationException($"Expected worker failure for {operationId}.");
}
