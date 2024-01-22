using NTDLS.Semaphore;

namespace NTDLS.DelegateThreadPool
{
    /// <summary>
    /// Creates an active thread pool where work items can be queued as delegate functions.
    /// </summary>
    public class DelegateThreadPool : IDisposable
    {
        /// <summary>
        /// The delegate prototype for the work queue.
        /// </summary>
        public delegate void ThreadAction();

        /// <summary>
        /// The number of threads in the thread pool.
        /// </summary>
        public int ThreadCount { get; private set; }

        private readonly List<Thread> _threads = new();
        private readonly PessimisticCriticalResource<Queue<QueueItemState>> _actions = new();
        private readonly AutoResetEvent _queueWaitEvent = new(true);
        private bool _keepRunning = false;

        /// <summary>
        /// Defines the pool size and starts the workr threads.
        /// </summary>
        /// <param name="threadCount">The number of threads to create.</param>
        public DelegateThreadPool(int threadCount)
        {
            ThreadCount = threadCount;
            _keepRunning = true;

            for (int i = 0; i < threadCount; i++)
            {
                var thread = new Thread(InternalThreadProc);
                _threads.Add(thread);
                thread.Start();
            }
        }

        /// <summary>
        /// Creates a queue token collection. This allows you to keep track of a set
        /// of items that have been queued so that you can wait on them to complete.
        /// </summary>
        /// <returns></returns>
        public QueueItemStateCollection CreateQueueStateCollection()
        {
            return new QueueItemStateCollection(this);
        }

        /// <summary>
        /// Enqueues a work item into the thread pool.
        /// </summary>
        /// <param name="threadAction">The delegate function to execute when a thread is ready.</param>
        /// <returns>Returns a token item that allows you to wait on completion or determine when the work item has been processed</returns>
        public QueueItemState Enqueue(ThreadAction threadAction)
        {
            return _actions.Use(o =>
            {
                var queueToken = new QueueItemState(threadAction);
                o.Enqueue(queueToken);
                _queueWaitEvent.Set();
                return queueToken;
            });
        }

        /// <summary>
        /// Stops the threads in the pool after they are all complete. This is also called by Dispose();
        /// </summary>
        public void Stop()
        {
            if (_keepRunning)
            {
                _keepRunning = false;
                _threads.ForEach(o => o.Join());
                _threads.Clear();
            }
        }

        private void InternalThreadProc()
        {
            int tryCount = 0;

            while (_keepRunning)
            {
                var queueToken = _actions.Use(o =>
                {
                    o.TryDequeue(out var dequeued);
                    return dequeued;
                });

                if (queueToken != null)
                {
                    queueToken.ThreadAction();
                    queueToken.SetComplete();
                }

                if ((++tryCount % 10000) == 0)
                {
                    _queueWaitEvent.WaitOne(1);
                }
            }
        }

        /// <summary>
        /// Stops the threads in the pool after they are all complete.
        /// </summary>
        public void Dispose()
        {
            Stop();
        }
    }
}

