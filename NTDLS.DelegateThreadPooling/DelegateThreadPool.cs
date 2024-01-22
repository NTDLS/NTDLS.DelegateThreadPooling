using NTDLS.Semaphore;

namespace NTDLS.DelegateThreadPooling
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
        /// The delegate prototype for the work queue.
        /// </summary>
        /// <param name="parameter">The user supplied parameter that will be passed to the delegate function.</param>
        public delegate void ParameterizedThreadAction(object? parameter);

        /// <summary>
        /// The maximum number of items that can be in the queue at a time. Additional calls to enqueue will block.
        /// </summary>
        public int MaxQueueDepth { get; set; }

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
        private readonly AutoResetEvent _itemQueuedWaitEvent = new(true);
        private readonly AutoResetEvent _itemDequeuedWaitEvent = new(true);
        internal bool KeepRunning { get; private set; } = false;

        /// <summary>
        /// Starts n worker threads, where n is the number of CPU cores available to the operating system.
        /// </summary>
        public DelegateThreadPool()
        {
            ThreadCount = Environment.ProcessorCount;
            KeepRunning = true;

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
        ///<param name="maxQueueDepth">The maximum number of items that can be queued before blocking. Zero indicates unlimited.</param>
        public DelegateThreadPool(int threadCount, int maxQueueDepth)
        {
            if (maxQueueDepth < 0)
            {
                maxQueueDepth = 0;
            }

            ThreadCount = threadCount;
            MaxQueueDepth = maxQueueDepth;
            KeepRunning = true;

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
            MaxQueueDepth = 0;
            KeepRunning = true;

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
            if (KeepRunning == false)
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
            //Enforce max queue depth size.
            if (MaxQueueDepth > 0)
            {
                uint tryCount = 0;

                while (KeepRunning)
                {
                    int queueSize = _actions.Use(o => o.Count);
                    if (queueSize < MaxQueueDepth)
                    {
                        break;
                    }

                    if (tryCount++ == SpinCount)
                    {
                        tryCount = 0;
                        //Wait for a small amount of time or until the event is signaled (which 
                        //indicates that an item has been dequeued thefrby creating free space).
                        _itemDequeuedWaitEvent.WaitOne(WaitDuration);
                    }
                }

                if (KeepRunning == false)
                {
                    throw new Exception("The thread pool is shutting down.");
                }
            }

            return _actions.Use(o =>
            {
                var queueToken = new QueueItemState(this, threadAction);
                o.Enqueue(queueToken);
                _itemQueuedWaitEvent.Set();
                return queueToken;
            });
        }

        /// <summary>
        /// Enqueues a work item into the thread pool.
        /// </summary>
        /// <param name="parameter">User supplied parameter that will be passed to the delegate function.</param>
        /// <param name="parameterizedThreadAction">The delegate function to execute when a thread is ready.</param>
        /// <returns>Returns a token item that allows you to wait on completion or determine when the work item has been processed</returns>
        public QueueItemState Enqueue(object? parameter, ParameterizedThreadAction parameterizedThreadAction)
        {
            //Enforce max queue depth size.
            if (MaxQueueDepth > 0)
            {
                uint tryCount = 0;

                while (KeepRunning)
                {
                    int queueSize = _actions.Use(o => o.Count);
                    if (queueSize < MaxQueueDepth)
                    {
                        break;
                    }

                    if (tryCount++ == SpinCount)
                    {
                        tryCount = 0;
                        //Wait for a small amount of time or until the event is signaled (which 
                        //indicates that an item has been dequeued thefrby creating free space).
                        _itemDequeuedWaitEvent.WaitOne(WaitDuration);
                    }
                }

                if (KeepRunning == false)
                {
                    throw new Exception("The thread pool is shutting down.");
                }
            }

            return _actions.Use(o =>
            {
                var queueToken = new QueueItemState(this, parameter, parameterizedThreadAction);
                o.Enqueue(queueToken);
                _itemQueuedWaitEvent.Set();
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
            if (KeepRunning)
            {
                KeepRunning = false;
                _threads.ForEach(o => o.Join());
                _threads.Clear();
            }
        }

        private void InternalThreadProc()
        {
            uint tryCount = 0;

            while (KeepRunning)
            {
                var queueToken = _actions.Use(o =>
                {
                    if (o.TryDequeue(out var dequeued))
                    {
                        //Enqueue might be blocking due to enforcing max queue depth size, tell it that the queue size has decreased.
                        _itemDequeuedWaitEvent.Set();
                    }
                    if (dequeued?.IsComplete == true)
                    {
                        return null; //Queued items where IsComplete == true have been aborted, just ignore them.
                    }
                    return dequeued;
                });

                if (queueToken != null)
                {
                    try
                    {
                        if (queueToken.ThreadAction != null)
                        {
                            queueToken.ThreadAction();
                        }
                        else if (queueToken.ParameterizedThreadAction != null)
                        {
                            queueToken.ParameterizedThreadAction(queueToken.Parameter);
                        }
                    }
                    catch (Exception ex)
                    {
                        queueToken.SetException(ex);
                    }
                    queueToken.SetComplete();
                }

                if (tryCount++ == SpinCount)
                {
                    tryCount = 0;
                    _itemQueuedWaitEvent.WaitOne(WaitDuration);
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

