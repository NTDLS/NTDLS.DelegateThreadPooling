using NTDLS.Semaphore;
using static NTDLS.DelegateThreadPooling.PooledThreadEnvelope;

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
        /// The delegate prototype for completed work queue items.
        /// </summary>
        public delegate void ThreadCompleteAction();

        /// <summary>
        /// The delegate prototype for periodic updates.
        /// </summary>
        /// <returns>Return true to continue, return false to stop waiting. Canceling the wait does not cancel the queued workers.</returns>
        public delegate bool PeriodicUpdateAction();

        /// <summary>
        /// The delegate prototype for the work queue.
        /// </summary>
        /// <param name="parameter">The user supplied parameter that will be passed to the delegate function.</param>
        public delegate void ParameterizedThreadAction(object? parameter);

        private readonly List<PooledThreadEnvelope> _threadEnvelopes = new();

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
        public int SpinCount { get; set; } = 100;

        /// <summary>
        /// The number of milliseconds to wait for queued items after each expiration of SpinCount.
        /// </summary>
        public int WaitDuration { get; set; } = 1;

        private readonly PessimisticCriticalResource<Queue<QueueItemState>> _actions = new();
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
                _threadEnvelopes.Add(new PooledThreadEnvelope(InternalThreadProc));
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
                _threadEnvelopes.Add(new PooledThreadEnvelope(InternalThreadProc));
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
                _threadEnvelopes.Add(new PooledThreadEnvelope(InternalThreadProc));
            }
        }

        /// <summary>
        /// Creates a QueueItemState collection. This allows you to keep track of a set
        /// of items that have been queued so that you can wait on them to complete.
        /// </summary>
        /// <returns></returns>
        public QueueItemStates CreateQueueStateTracker()
        {
            return new QueueItemStates(this);
        }

        /// <summary>
        /// Enqueues a work item into the thread pool.
        /// </summary>
        /// <param name="threadAction">The delegate function to execute when a thread is ready.</param>
        /// /// <param name="onComplete">The delegate function to call when the queue item is finished processing.</param>
        /// <returns>Returns a token item that allows you to wait on completion or determine when the work item has been processed</returns>
        public QueueItemState Enqueue(ThreadAction threadAction, ThreadCompleteAction? onComplete = null)
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
                    throw new DelegateThreadPoolShuttingDown("The thread pool is shutting down.");
                }
            }

            return _actions.Use(o =>
            {
                var queueToken = new QueueItemState(this, threadAction, onComplete);
                o.Enqueue(queueToken);
                SignalIdleThread();
                return queueToken;
            });
        }

        /// <summary>
        /// Enqueues a work item into the thread pool.
        /// </summary>
        /// <param name="parameter">User supplied parameter that will be passed to the delegate function.</param>
        /// <param name="parameterizedThreadAction">The delegate function to execute when a thread is ready.</param>
        /// /// <param name="onComplete">The delegate function to call when the queue item is finished processing.</param>
        /// <returns>Returns a token item that allows you to wait on completion or determine when the work item has been processed</returns>
        public QueueItemState Enqueue(object? parameter, ParameterizedThreadAction parameterizedThreadAction, ThreadCompleteAction? onComplete = null)
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
                    throw new DelegateThreadPoolShuttingDown("The thread pool is shutting down.");
                }
            }

            return _actions.Use(o =>
            {
                var queueToken = new QueueItemState(this, parameter, parameterizedThreadAction, onComplete);
                o.Enqueue(queueToken);
                SignalIdleThread();
                return queueToken;
            });
        }

        private void SignalIdleThread()
        {
            //Find the first idle thread and signal it. Its ok if we dont find one, because the first
            //  thread to complete its workload will automatically pickup the next item in the queue.
            _threadEnvelopes.Where(o => o.State == PooledThreadState.Idle).FirstOrDefault()?.Signal();
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
                _threadEnvelopes.ForEach(o =>
                {
                    o.Signal();
                    o.Join();
                });
                _threadEnvelopes.Clear();
            }
        }

        private void InternalThreadProc(object? internalThreadObj)
        {
            if (internalThreadObj == null)
            {
                throw new Exception("Thread manager was not supplied to thread proc.");
            }

            var threadEnvelope = (PooledThreadEnvelope)internalThreadObj;

            uint tryDequeueCount = 0;

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

                if (KeepRunning && queueToken != null)
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

                    tryDequeueCount = 0; //Rest the spin count if the thread dequeued an item.
                }
                else if (KeepRunning && tryDequeueCount++ >= SpinCount)
                {
                    //We have tried to dequeue too many times without success, just sleep.

                    threadEnvelope.State = PooledThreadState.Idle;
                    if (threadEnvelope.Wait())
                    {
                        threadEnvelope.State = PooledThreadState.Executing;
                        tryDequeueCount = 0; //Rest the spin count if the thread was signaled.
                    }
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
