using NTDLS.DelegateThreadPooling;

namespace TestHarness
{
    internal class Program
    {
        private static readonly DelegateThreadPool _delegateThreadPool = new(10);

        static void Main()
        {
            ChildQueueWithTypedDelegateExample();

            ChildQueueExample();

            CollectionExample();

            Console.WriteLine("Press [enter] to exit.");
            Console.ReadLine();

            _delegateThreadPool.Dispose();
        }

        #region Child Queue with Typed Delegate Example.

        private class TypedDelegateParam
        {
            public int SomeParameter1 { get; set; }
            public int SomeParameter2 { get; set; }
        }

        private static void TypedDelegateThread(TypedDelegateParam param)
        {
            Thread.Sleep(100);
        }

        private static void ChildQueueWithTypedDelegateExample()
        {
            Console.WriteLine("TypedDelegateExample: Starting to enqueue items...");

            var childPool = _delegateThreadPool.CreateChildPool<TypedDelegateParam>();

            //Enqueue work items as delegate functions.
            for (int i = 0; i < 100; i++)
            {
                var param = new TypedDelegateParam()
                {
                    SomeParameter1 = i,
                    SomeParameter2 = i * 2
                };

                childPool.Enqueue(param, TypedDelegateThread);
            }

            Console.WriteLine("Enqueue complete, waiting on completion.");

            //Wait on all of the work items to complete.
            childPool.WaitForCompletion();

            Console.WriteLine("All workers are complete.");
        }

        #endregion

        #region Child Queue Example.

        private static void ChildQueueExample()
        {
            Console.WriteLine("ChildQueueExample: Starting to enqueue items...");

            var childPool = _delegateThreadPool.CreateChildPool();

            //Enqueue work items as delegate functions.
            for (int i = 0; i < 100; i++)
            {
                childPool.Enqueue(() =>
                {
                    Thread.Sleep(100); //Do some work...
                });
            }

            Console.WriteLine("Enqueue complete, waiting on completion.");

            //Wait on all of the work items to complete.
            childPool.WaitForCompletion();

            Console.WriteLine("All workers are complete.");
        }

        #endregion

        #region Collection Example.

        private static void CollectionExample()
        {
            Console.WriteLine("CollectionExample: Starting to enqueue items...");

            var itemStates = new List<QueueItemState<object>>();

            //Enqueue work items as delegate functions.
            for (int i = 0; i < 100; i++)
            {
                var queueItemState = _delegateThreadPool.Enqueue(() =>
                {
                    Thread.Sleep(100); //Do some work...
                });

                itemStates.Add(queueItemState);
            }

            Console.WriteLine("Enqueue complete, waiting on completion.");

            //Wait on all of the work items to complete.
            itemStates.ForEach(t => t.WaitForCompletion());

            Console.WriteLine("All workers are complete.");
        }

        #endregion
    }
}
