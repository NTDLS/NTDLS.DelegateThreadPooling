# NTDLS.DelegateThreadPooling

A very high-performance, active thread pool for .NET where work items are queued as plain delegate functions. You get a FIFO queue with an optional maximum depth, wait handles for individual items or whole batches, automatic growth and shrink of the worker threads, per-pool configuration, and multiple independent pools so different workloads do not interfere with one another. It targets .NET 8.0, 9.0, and 10.0.

📦 Be sure to check out the NuGet package: https://www.nuget.org/packages/NTDLS.DelegateThreadPooling

## When to use it

If you have ever been frustrated with **System.Threading.ThreadPool**, this is likely the solution you are looking for. Consider this package when you need any of the following:

- **Predictable parallelism.** `ThreadPool` gives you no control over how many threads run your work. Here you set the initial and maximum thread counts explicitly (or let auto-growth/auto-shrink manage them within those bounds).
- **Bounded queues.** Set `MaximumQueueDepth` (pool-wide) or `MaxChildQueueDepth` (per batch) so that producers block instead of suffering unbounded memory growth when consumers fall behind.
- **Waiting on batches.** Create a child pool, enqueue a collection of work items, and call `WaitForCompletion()` once — instead of juggling dozens of `Task`s or manual synchronization.
- **Isolated workloads.** Run multiple `DelegateThreadPool` instances side by side so a burst in one workload cannot starve another.
- **Per-item state and diagnostics.** Every enqueued item returns a `QueueItemState<T>` exposing start time, completion duration, CPU (`ProcessorTime`) consumed, exceptions, and the ability to abort queued-but-not-started items. Child pools additionally track `TotalDurationMs` and `TotalProcessorTimeMs` across the whole batch.
- **Cancellation of queued work.** Items that have not started yet can be aborted individually (`Abort()`) or in bulk via a child pool's `Abort()`.

## Usage

### Creating a pool

```cs
// Default: one worker thread per available CPU core.
using var pool = new DelegateThreadPool();

// Or configure it explicitly:
var config = new DelegateThreadPoolConfiguration
{
    InitialThreadCount = 4,        // Workers started at construction.
    MaximumThreadCount = 16,       // Upper bound for auto-growth.
    MaximumQueueDepth = 10_000,    // Enqueue blocks above this depth (0 = unlimited).
};
using var configuredPool = new DelegateThreadPool(config);
```

### Enqueueing work and waiting on individual items

```cs
// Each Enqueue returns a QueueItemState you can wait on later.
var states = new List<QueueItemState<object>>();

for (int i = 0; i < 100; i++)
{
    // Work runs on a pooled thread; parameterized overloads exist too.
    states.Add(pool.Enqueue(() => Thread.Sleep(10)));
}

// Block until every item has been processed.
states.ForEach(s => s.WaitForCompletion());

// Inspect results: s.IsComplete, s.CompletionTime, s.ProcessorTime,
// s.ExceptionOccurred / s.Exception, or cancel queued work with s.Abort().
```

### Batching work with a child pool

```cs
// A child pool groups a set of items so you can wait on the whole batch.
var batch = pool.CreateChildPool();

for (int i = 0; i < 100; i++)
{
    batch.Enqueue(() => Thread.Sleep(10));
}

// Wait for the entire batch; throws an AggregateException if any item faulted.
batch.WaitForCompletion();

// Batch-level performance counters are tracked automatically.
Console.WriteLine($"{batch.TotalDurationMs} ms wall, {batch.TotalProcessorTimeMs} ms CPU");
```

### Parameterized work items

```cs
// The typed overload passes a value straight into the delegate.
var state = pool.Enqueue(42, (int value) => Console.WriteLine(value));
state.WaitForCompletion();
```

### Progress reporting while waiting

```cs
// WaitForCompletion(updateDelay, callback) invokes the callback periodically.
// Return false from the callback to stop waiting (already-queued items keep running).
bool completed = batch.WaitForCompletion(TimeSpan.FromMilliseconds(500), () =>
{
    Console.Write(".");
    return true; // Keep waiting.
});
```

### Multiple isolated pools

```cs
// Independent pools keep separate workloads from interfering with each other.
using var ioPool = new DelegateThreadPool(new DelegateThreadPoolConfiguration { InitialThreadCount = 8 });
using var cpuPool = new DelegateThreadPool(new DelegateThreadPoolConfiguration { InitialThreadCount = Environment.ProcessorCount });

ioPool.Enqueue(() => DoIoWork());
cpuPool.Enqueue(() => DoCpuWork());
```

### Disposal

The pool implements `IDisposable`; disposing stops the worker threads after they finish their current work. Prefer a `using` statement wherever the pool has a well-defined lifetime.

## License

[MIT](https://choosealicense.com/licenses/mit/)
