using Microsoft.JSInterop;

namespace ValidationWorker;

public sealed class WebWorkerClient(IJSObjectReference worker) : IAsyncDisposable
{
    private const int DefaultTimeoutMs = 60000;
    private static readonly string DefaultAssemblyName = typeof(WebWorkerClient).Assembly.GetName().Name!;

    public static async Task<WebWorkerClient> CreateAsync(
        IJSRuntime jsRuntime,
        int timeoutMs = DefaultTimeoutMs,
        string? assemblyName = null,
        CancellationToken cancellationToken = default,
        int initializationDelayMs = 0)
    {
        IJSObjectReference? workerRef = null;

        try
        {
            await using var module = await jsRuntime.InvokeAsync<IJSObjectReference>(
                "import", cancellationToken, "./_content/ValidationWorker/dotnet-web-worker-client.js");

            cancellationToken.ThrowIfCancellationRequested();
            var resolvedName = assemblyName ?? DefaultAssemblyName;
            var options = new { assemblyName = resolvedName, initializationDelayMs };
            workerRef = await module.InvokeAsync<IJSObjectReference>("create", options);
            await workerRef.InvokeVoidAsync("initialize", cancellationToken, timeoutMs);

            return new WebWorkerClient(workerRef);
        }
        catch (JSException ex)
        {
            await CleanupFailedCreationAsync(workerRef);
            throw new InvalidOperationException("Unable to create the web worker JavaScript object.", ex);
        }
        catch
        {
            await CleanupFailedCreationAsync(workerRef);
            throw;
        }
    }

    public static async ValueTask<int> GetActiveWorkerCountAsync(IJSRuntime jsRuntime)
    {
        try
        {
            await using var module = await jsRuntime.InvokeAsync<IJSObjectReference>(
                "import", "./_content/ValidationWorker/dotnet-web-worker-client.js");
            return await module.InvokeAsync<int>("getActiveWorkerCount");
        }
        catch (JSException ex)
        {
            throw new InvalidOperationException("Unable to read the active web worker count.", ex);
        }
    }

    private static async ValueTask CleanupFailedCreationAsync(IJSObjectReference? workerRef)
    {
        if (workerRef is null)
        {
            return;
        }

        try
        {
            await workerRef.InvokeVoidAsync("terminate");
        }
        catch (JSDisconnectedException)
        {
            // The browser context is already gone.
        }

        await workerRef.DisposeAsync();
    }

    // Invokes a [JSExport] method from the web worker.
    // The method string is the fully qualified path: "AssemblyName.ClassName.MethodName".
    // Arguments and return values must be primitive types or strings.
    public async Task<TResult> InvokeAsync<TResult>(string method, object[] args, int timeoutMs = DefaultTimeoutMs, CancellationToken cancellationToken = default)
    {
        try
        {
            return await worker.InvokeAsync<TResult>("invoke", cancellationToken, [method, args, timeoutMs]);
        }
        catch (JSException ex)
        {
            throw new InvalidOperationException("Unable to invoke the web worker JavaScript function.", ex);
        }
    }

    public async Task InvokeVoidAsync(string method, object[] args, int timeoutMs = DefaultTimeoutMs, CancellationToken cancellationToken = default)
    {
        try
        {
            await worker.InvokeVoidAsync("invoke", cancellationToken, [method, args, timeoutMs]);
        }
        catch (JSException ex)
        {
            throw new InvalidOperationException("Unable to invoke the web worker JavaScript function.", ex);
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await worker.InvokeVoidAsync("terminate");
        }
        catch (JSDisconnectedException)
        {
            // JS interop disconnected, worker is already gone
        }

        await worker.DisposeAsync();
    }
}
