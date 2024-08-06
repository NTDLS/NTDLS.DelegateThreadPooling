using NTDLS.DelegateThreadPooling;

namespace TestHarness
{
    internal class Program
    {
        private static DelegateThreadPool _delegateThreadPool = new(10);

        static void Main()
        {
            TypedDelegateExample();
            //CollectionExample();
            //NoCollectionExample();

            Console.WriteLine("Press [enter] to exit.");
            Console.ReadLine();

            _delegateThreadPool.Dispose();
        }

        class TypedDelegateParam
        {
        }

        private static void TypedDelegateThread(TypedDelegateParam? param)
        {
        }


        private static void TypedDelegateExample()
        {
            Console.WriteLine("TypedDelegateExample: Starting to enqueue items...");

            var queuedStates = _delegateThreadPool.CreateQueueStateTracker< TypedDelegateParam>();

            //Enqueue work items as delegate functions.
            for (int i = 0; i < 100; i++)
            {
                var param = new TypedDelegateParam();

                queuedStates.Enqueue(param, TypedDelegateThread);
            }

            Console.WriteLine("Enqueue complete, waiting on completion.");

            //Wait on all of the work items to complete.
            queuedStates.WaitForCompletion();

            Console.WriteLine("All workers are complete.");
        }

        /*
        private static void CollectionExample()
        {
            Console.WriteLine("CollectionExample: Starting to enqueue items...");

            var queuedStates = _delegateThreadPool.CreateQueueStateTracker();

            //Enqueue work items as delegate functions.
            for (int i = 0; i < 100; i++)
            {
                queuedStates.Enqueue(() =>
                {
                    Thread.Sleep(1000); //Do some work...
                });
            }

            Console.WriteLine("Enqueue complete, waiting on completion.");

            //Wait on all of the work items to complete.
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

            //Wait on all of the work items to complete.
            queueTokens.ForEach(t => t.WaitForCompletion());

            Console.WriteLine("All workers are complete.");
        }
        */
    }
}
