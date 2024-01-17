# NTDLS.DelegateThreadPool

📦 Be sure to check out the NuGet pacakge: https://www.nuget.org/packages/NTDLS.DelegateThreadPool

Active thread pool where work items can be queued as delegate functions. Allows you to easily
enqueue infinite FIFO worker items and wait on collections of those items to complete.

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
