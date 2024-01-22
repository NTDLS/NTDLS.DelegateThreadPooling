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

        /// <summary>
        /// The number of times to repeatedly check the internal lock availability before going to going to sleep.
        /// </summary>
        public int SpinCount { get; set; } = 100000;

        /// <summary>
        /// The number of milliseconds to wait for queued items after each expiration of SpinCount.
        /// </summary>
        public int WaitDuration { get; set; } = 1;

        private readonly List<Thread> _threads = new();
        private readonly PessimisticCriticalResource<Queue<QueueItemState>> _actions = new();
        private readonly AutoResetEvent _queueWaitEvent = new(true);
        private bool _keepRunning = false;

        /// <summary>
        /// Starts n worker threads, where n is the number of CPU cores available to the operating system.
        /// </summary>
        public DelegateThreadPool()
        {
            ThreadCount = Environment.ProcessorCount;
            _keepRunning = true;

            for (int i = 0; i < ThreadCount; i++)
            {
                var thread = new Thread(InternalThreadProc);
                _threads.Add(thread);
                thread.Start();
            }
        }

        /// <summary>
        /// Defines the pool size and starts the worker threads.
        /// </summary>
        /// <param name="threadCount">The number of threads to create.</param>
        public DelegateThreadPool(int threadCount)
        {
            ThreadCount = threadCount;
            _keepRunning = true;

            for (int i = 0; i < ThreadCount; i++)
            {
                var thread = new Thread(InternalThreadProc);
                _threads.Add(thread);
                thread.Start();
            }
        }

        /// <summary>
        /// Adds additional threads to the thread pool.
        /// </summary>
        /// <param name="additionalThreads">The number of threads to add.</param>
        /// <exception cref="Exception"></exception>
        public void Grow(int additionalThreads)
        {
            if (_keepRunning == false)
            {
                throw new Exception("The thread pool is not running.");
            }

            ThreadCount += additionalThreads;

            for (int i = 0; i < additionalThreads; i++)
            {
                var thread = new Thread(InternalThreadProc);
                _threads.Add(thread);
                thread.Start();
            }
        }

        /// <summary>
        /// Creates a QueueItemState collection. This allows you to keep track of a set
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
                var queueToken = new QueueItemState(this, threadAction);
                o.Enqueue(queueToken);
                _queueWaitEvent.Set();
                return queueToken;
            });
        }

        /// <summary>
        /// Cancels a given queued worker item. 
        /// </summary>
        /// <param name="item"></param>
        /// <returns>Returns true if the item was cancelled.</returns>
        public bool Abort(QueueItemState item)
        {
            return item.Abort();
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
                    if (dequeued?.IsComplete == true)
                    {
                        return null; //Queued items where IsComplete == true have been aborted.
                    }
                    return dequeued;
                });

                if (queueToken != null)
                {
                    try
                    {
                        queueToken.ThreadAction();
                    }
                    catch (Exception ex)
                    {
                        queueToken.SetException(ex);
                    }
                    queueToken.SetComplete();
                }

                if ((++tryCount % SpinCount) == 0)
                {
                    _queueWaitEvent.WaitOne(WaitDuration);
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

