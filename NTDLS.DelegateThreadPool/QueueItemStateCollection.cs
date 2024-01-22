using static NTDLS.DelegateThreadPool.DelegateThreadPool;

namespace NTDLS.DelegateThreadPool
{
    /// <summary>
    /// Contains a collection of queue tokens. Allows for determining when a set of queued items have been completed.
    /// </summary>
    public class QueueItemStateCollection
    {
        /// <summary>
        /// The collection of enqueued work items.
        /// </summary>
        public List<QueueItemState> Collection { get; private set; } = new();
        private readonly DelegateThreadPool _threadPool;

        internal QueueItemStateCollection(DelegateThreadPool threadPool)
        {
            _threadPool = threadPool;
        }

        /// <summary>
        /// Adds a delegate function to the work queue.
        /// </summary>
        /// <param name="threadAction">Returns a token that allows for waiting on the queued item.</param>
        /// <returns></returns>
        public QueueItemState Enqueue(ThreadAction threadAction)
        {
            var queueToken = _threadPool.Enqueue(threadAction);
            Collection.Add(queueToken);
            return queueToken;
        }

        /// <summary>
        /// Cancels all queued worker items.
        /// </summary>
        /// <returns>Returns true if all item were cancelled.</returns>
        public bool Abort()
        {
            return Collection.All(o => o.Abort());
        }

        /// <summary>
        /// Blocks until all work items in the collection have been processed by a thread.
        /// </summary>
        public void WaitForCompletion()
        {
            while (_threadPool.KeepRunning && Collection.All(o => o.WaitForCompletion()) == false)
            {
                Thread.Yield();
            }

            if (_threadPool.KeepRunning == false)
            {
                throw new Exception("The thread pool is shutting down.");
            }
        }
    }
}
