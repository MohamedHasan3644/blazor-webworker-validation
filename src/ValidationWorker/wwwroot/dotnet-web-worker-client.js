const activeWorkers = new Set();

function withTimeout(promise, timeoutMs, timeoutMessage) {
    let timeoutId;
    const timeout = new Promise((_, reject) => {
        timeoutId = setTimeout(() => reject(new Error(timeoutMessage)), timeoutMs);
    });

    return Promise.race([promise, timeout]).finally(() => clearTimeout(timeoutId));
}

class DotnetWebWorkerClient {
    #worker;
    #initialization;
    #initializationDelayId;
    #pendingRequests = {};
    #requestId = 0;

    constructor(worker, options = {}) {
        this.#worker = worker;
        activeWorkers.add(worker);

        this.#initialization = new Promise((resolve, reject) => {
            worker.addEventListener('error', (e) => {
                const error = new Error(e.message || 'Worker encountered an error');
                reject(error);
                this.rejectAllPending(error.message);
            });
            worker.addEventListener('message', function onMessage(e) {
                if (e.data.type === "ready") {
                    worker.removeEventListener('message', onMessage);
                    e.data.error ? reject(new Error(e.data.error)) : resolve();
                }
            });
        });

        this.setupMessageHandler();

        const dotnetJsUrl = DotnetWebWorkerClient.resolveDotnetJsUrl();
        const assemblyName = options?.assemblyName ?? null;
        const initializationDelayMs = Math.max(0, options?.initializationDelayMs ?? 0);
        this.#initializationDelayId = setTimeout(() => {
            this.#initializationDelayId = null;
            worker.postMessage({ type: 'init', dotnetJsUrl, assemblyName });
        }, initializationDelayMs);
    }

    static create(options = {}) {
        const worker = new Worker('_content/ValidationWorker/dotnet-web-worker.js', { type: "module" });
        return new DotnetWebWorkerClient(worker, options);
    }

    initialize(initTimeoutMs) {
        return withTimeout(this.#initialization, initTimeoutMs, 'Worker initialization timed out').catch(err => {
            this.terminate();
            throw err;
        });
    }

    static resolveDotnetJsUrl() {
        // Resolve using the browser's import map (handles fingerprinted URLs in published apps).
        // Workers don't inherit the page's import map, so we resolve on the main thread and pass the URL.
        const dotnetJsUrl = new URL('_framework/dotnet.js', document.baseURI).href;
        return import.meta.resolve?.(dotnetJsUrl) ?? dotnetJsUrl;
    }

    invoke(method, args, timeoutMs) {
        const id = ++this.#requestId;
        const invoke = new Promise((resolve, reject) => {
            this.#pendingRequests[id] = { resolve, reject };
            this.#worker.postMessage({ method, args, requestId: id });
        });

        return withTimeout(invoke, timeoutMs, `Worker method '${method}' timed out`).catch(err => {
            if (this.#pendingRequests[id]) {
                delete this.#pendingRequests[id];
            }
            throw err;
        });
    }

    terminate() {
        this.rejectAllPending("Worker terminated");
        if (this.#initializationDelayId !== null) {
            clearTimeout(this.#initializationDelayId);
            this.#initializationDelayId = null;
        }

        if (this.#worker) {
            this.#worker.terminate();
            activeWorkers.delete(this.#worker);
        }

        this.#worker = null;
    }

    setupMessageHandler() {
        this.#worker.addEventListener('message', (e) => {
            if (e.data.type === "result") {
                const request = this.#pendingRequests[e.data.requestId];
                if (request) {
                    delete this.#pendingRequests[e.data.requestId];
                    if (e.data.error) {
                        request.reject(new Error(e.data.error));
                    } else {
                        request.resolve(e.data.result);
                    }
                }
            }
        });

    }

    rejectAllPending(errorMessage) {
        for (const id in this.#pendingRequests) {
            this.#pendingRequests[id].reject(new Error(errorMessage));
            delete this.#pendingRequests[id];
        }
    }
}

export function create(options) {
    return DotnetWebWorkerClient.create(options);
}

export function getActiveWorkerCount() {
    return activeWorkers.size;
}
