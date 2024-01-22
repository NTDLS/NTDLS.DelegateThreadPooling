# NTDLS.DelegateThreadPool

📦 Be sure to check out the NuGet pacakge: https://www.nuget.org/packages/NTDLS.DelegateThreadPool

High performance active thread pool where work items can be queued as delegate functions.
Allows you to easily enqueue infinite FIFO worker items or enforce queue size, wait on collections
of those items to complete, and total control over the pool size. Also allows for multiple pools,
so that different workloads do not interfere with one another.

_If you have ever been frustrated with **System.Threading.ThreadPool**, then this is likely the solution you are looking for._

```cs
private static readonly DelegateThreadPool _delegateThreadPool = new(10);

static void Main()
{
    CollectionExample();
    NoCollectionExample();

    Console.WriteLine("Press [enter] to exit.");
    Console.ReadLine();

    _delegateThreadPool.Dispose();
}

private static void CollectionExample()
{
    Console.WriteLine("CollectionExample: Starting to enqueue items...");

    var queuedStates = _delegateThreadPool.CreateQueueStateCollection();

    //Enqueue work items as delegate functions.
    for (int i = 0; i < 100; i++)
    {
        queuedStates.Enqueue(() =>
        {
            Thread.Sleep(1000); //Do some work...
        });
    }

    Console.WriteLine("Enqueue complete, waiting on completion.");

    //Wait on all of the workitems to complete.
    queuedStates.WaitForCompletion();

    Console.WriteLine("All workers are complete.");
}

private static void NoCollectionExample()
{
    Console.WriteLine("NoCollectionExample: Starting to enqueue items...");

    var queueTokens = new List<QueueItemState>();

    //Enqueue work items as delegate functions.
    for (int i = 0; i < 100; i++)
    {
        var queueItemState = _delegateThreadPool.Enqueue(() =>
        {
            Thread.Sleep(1000); //Do some work...
        });

        queueTokens.Add(queueItemState);
    }

    Console.WriteLine("Enqueue complete, waiting on completion.");

    //Wait on all of the workitems to complete.
    queueTokens.ForEach(t => t.WaitForCompletion());

    Console.WriteLine("All workers are complete.");
}
```
