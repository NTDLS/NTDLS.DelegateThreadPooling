using NTDLS.Semaphore;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using static NTDLS.DelegateThreadPooling.PooledThreadEnvelope;
using System.Linq;
using System.Runtime.InteropServices;

namespace NTDLS.DelegateThreadPooling
{
    /// <summary>
    /// Creates an active thread pool where work items can be queued as delegate functions.
    /// </summary>
    public class DelegateThreadPool : IDisposable
    {
        private static readonly ConcurrentDictionary<Type, MethodInfo> _reflectionCache = new();

        /// <summary>
        /// The delegate prototype for the work queue.
        /// </summary>
        public delegate void ThreadActionDelegate();

        /// <summary>
        /// The delegate prototype for completed work queue items.
        /// </summary>
        public delegate void ThreadCompleteActionDelegate<T>(QueueItemState<T> parameter);

        /// <summary>
        /// The delegate prototype for periodic updates.
        /// </summary>
        /// <returns>Return true to continue, return false to stop waiting. Canceling the wait does not cancel the queued workers.</returns>
        public delegate bool PeriodicUpdateActionDelegate();

        /// <summary>
        /// The delegate prototype for the work queue.
        /// </summary>
        /// <param name="parameter">The user supplied parameter that will be passed to the delegate function.</param>
        public delegate void ParameterizedThreadActionDelegate(object? parameter);

        /// <summary>
        /// The delegate prototype for the work queue.
        /// </summary>
        /// <param name="parameter">The user supplied parameter that will be passed to the delegate function.</param>
        public delegate void ParameterizedThreadActionDelegate<T>(T parameter);

        private readonly List<PooledThreadEnvelope> _threadEnvelopes = new();

        /// <summary>
        /// Provides read-only access to threads in the thread pool for diagnostics and performance reporting.
        /// </summary>
        public IReadOnlyList<PooledThreadEnvelope> Threads { get => _threadEnvelopes; }

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

        private readonly PessimisticCriticalResource<Queue<IQueueItemState>> _actions = new();
        internal AutoResetEvent ItemDequeuedWaitEvent { get; private set; } = new(true);
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
        public TrackableQueue<object> CreateChildQueue(int maxChildQueueDepth = 0)
        {
            return new TrackableQueue<object>(this, maxChildQueueDepth);
        }

        /// <summary>
        /// Creates a QueueItemState collection. This allows you to keep track of a set
        /// of items that have been queued so that you can wait on them to complete.
        /// </summary>
        /// <typeparam name="T">The type which will be passed to the parameterized thread delegate.</typeparam>
        public TrackableQueue<T> CreateChildQueue<T>(int maxChildQueueDepth = 0)
        {
            return new TrackableQueue<T>(this, maxChildQueueDepth);
        }

        #region Enqueue Non-Parameterized.

        /// <summary>
        /// Enqueues a work item into the thread pool.
        /// </summary>
        /// <param name="threadAction">The delegate function to execute when a thread is ready.</param>
        /// <param name="onComplete">The delegate function to call when the queue item is finished processing.</param>
        /// <returns>Returns a state item that allows you to wait on completion or determine when the work item has been processed</returns>
        private QueueItemState<T> EnqueueInternalNonParameterized<T>(ThreadActionDelegate threadAction, ThreadCompleteActionDelegate<T>? onComplete = null)
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
                        //indicates that an item has been dequeued thereby creating free space).
                        ItemDequeuedWaitEvent.WaitOne(WaitDuration);
                    }
                }

                if (KeepRunning == false)
                {
                    throw new DelegateThreadPoolShuttingDown("The thread pool is shutting down.");
                }
            }

            return _actions.Use(o =>
            {
                var itemState = new QueueItemState<T>(this, threadAction, onComplete);
                o.Enqueue(itemState);
                SignalIdleThread();
                return itemState;
            });
        }

        /// <summary>
        /// Enqueues a work item into the thread pool.
        /// </summary>
        /// <param name="threadAction">The delegate function to execute when a thread is ready.</param>
        /// <param name="onComplete">The delegate function to call when the queue item is finished processing.</param>
        /// <returns>Returns a state item that allows you to wait on completion or determine when the work item has been processed</returns>
        public QueueItemState<T> Enqueue<T>(ThreadActionDelegate threadAction, ThreadCompleteActionDelegate<T>? onComplete = null)
        {
            return EnqueueInternalNonParameterized<T>(threadAction, onComplete);
        }

        /// <summary>
        /// Enqueues a work item into the thread pool.
        /// </summary>
        /// <param name="threadAction">The delegate function to execute when a thread is ready.</param>
        /// <param name="onComplete">The delegate function to call when the queue item is finished processing.</param>
        /// <returns>Returns a state item that allows you to wait on completion or determine when the work item has been processed</returns>
        public QueueItemState<object> Enqueue(ThreadActionDelegate threadAction, ThreadCompleteActionDelegate<object>? onComplete = null)
        {
            return EnqueueInternalNonParameterized<object>(threadAction, onComplete);
        }

        #endregion

        #region Enqueue Parameterized.

        /// <summary>
        /// Enqueues a work item into the thread pool.
        /// </summary>
        /// <param name="parameter">User supplied parameter that will be passed to the delegate function.</param>
        /// <param name="parameterizedThreadAction">The delegate function to execute when a thread is ready.</param>
        /// <param name="onComplete">The delegate function to call when the queue item is finished processing.</param>
        /// <returns>Returns a state item that allows you to wait on completion or determine when the work item has been processed</returns>
        private QueueItemState<T> EnqueueInternalParameterized<T>(T? parameter,
            ParameterizedThreadActionDelegate<T> parameterizedThreadAction, ThreadCompleteActionDelegate<T>? onComplete = null)
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
                        //indicates that an item has been dequeued thereby creating free space).
                        ItemDequeuedWaitEvent.WaitOne(WaitDuration);
                    }
                }

                if (KeepRunning == false)
                {
                    throw new DelegateThreadPoolShuttingDown("The thread pool is shutting down.");
                }
            }

            return _actions.Use(o =>
            {
                var itemState = new QueueItemState<T>(this, parameter, parameterizedThreadAction, onComplete);
                o.Enqueue(itemState);
                SignalIdleThread();
                return itemState;
            });
        }

        /// <summary>
        /// Enqueues a work item into the thread pool.
        /// </summary>
        /// <param name="parameter">User supplied parameter that will be passed to the delegate function.</param>
        /// <param name="parameterizedThreadAction">The delegate function to execute when a thread is ready.</param>
        /// <param name="onComplete">The delegate function to call when the queue item is finished processing.</param>
        /// <returns>Returns a state item that allows you to wait on completion or determine when the work item has been processed</returns>
        public QueueItemState<T> Enqueue<T>(T? parameter, ParameterizedThreadActionDelegate<T> parameterizedThreadAction, ThreadCompleteActionDelegate<T>? onComplete = null)
        {
            return EnqueueInternalParameterized(parameter, parameterizedThreadAction, onComplete);
        }

        /// <summary>
        /// Enqueues a work item into the thread pool.
        /// </summary>
        /// <param name="parameter">User supplied parameter that will be passed to the delegate function.</param>
        /// <param name="parameterizedThreadAction">The delegate function to execute when a thread is ready.</param>
        /// <param name="onComplete">The delegate function to call when the queue item is finished processing.</param>
        /// <returns>Returns a state item that allows you to wait on completion or determine when the work item has been processed</returns>
        public QueueItemState<object> Enqueue(object? parameter, ParameterizedThreadActionDelegate<object> parameterizedThreadAction, ThreadCompleteActionDelegate<object>? onComplete = null)
        {
            return EnqueueInternalParameterized(parameter, parameterizedThreadAction, onComplete);
        }

        #endregion

        private void SignalIdleThread()
        {
            //Find the first idle thread and signal it. Its ok if we don't find one, because the first
            //  thread to complete its workload will automatically pickup the next item in the queue.
            _threadEnvelopes.Where(o => o.State == PooledThreadState.Waiting).FirstOrDefault()?.Signal();
        }

        /// <summary>
        /// Cancels a given queued worker item. 
        /// </summary>
        /// <param name="item"></param>
        /// <returns>Returns true if the item was cancelled.</returns>
        public bool Abort<T>(QueueItemState<T> item)
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

        [DllImport("kernel32.dll")]
        private static extern int GetCurrentThreadId();

        /// <summary>
        /// Executes the enqueued delegates, both parameterized and non-parameterized.
        /// </summary>
        /// <param name="internalThreadObj"></param>
        private void InternalThreadProc(object? internalThreadObj)
        {
            if (internalThreadObj == null)
            {
                throw new Exception("Thread manager was not supplied to internal thread proc.");
            }

            var threadEnvelope = (PooledThreadEnvelope)internalThreadObj;

            #region Diagnostics.

            int nativeThreadId = GetCurrentThreadId();
            var process = Process.GetCurrentProcess();
            foreach (ProcessThread processThread in process.Threads)
            {
                if (processThread.Id == nativeThreadId)
                {
                    threadEnvelope.NativeThread = processThread;
                    break;
                }
            }

            #endregion

            uint tryDequeueCount = 0;
            while (KeepRunning)
            {
                var itemState = _actions.Use(o =>
                {
                    if (o.TryDequeue(out var dequeued))
                    {
                        //Enqueue might be blocking due to enforcing max queue depth size, tell it that the queue size has decreased.
                        ItemDequeuedWaitEvent.Set();
                    }
                    if (dequeued?.IsComplete == true)
                    {
                        //Queued items where IsComplete == true have been aborted, just ignore them.
                        return null;
                    }
                    return dequeued;
                });

                if (KeepRunning && itemState != null)
                {
                    var startTotalProcessorTime = threadEnvelope.NativeThread?.TotalProcessorTime;

                    try
                    {
                        if (itemState.ThreadAction != null)
                        {
                            itemState.StartTimestamp = DateTime.UtcNow;

                            itemState.ThreadAction();
                        }
                        else if (itemState.ParameterizedThreadAction != null)
                        {
                            //Since we are using reflection to execute the ParameterizedThreadAction,
                            //  we are going to use a ConcurrentDictionary to cache to the invoke method.
                            var actionType = itemState.ParameterizedThreadAction.GetType();
                            if (!_reflectionCache.TryGetValue(actionType, out var method))
                            {
                                if ((method = actionType.GetMethod("Invoke")) != null)
                                {
                                    _reflectionCache[actionType] = method;
                                }
                            }

                            //We are using reflection so that the user code can enforce the type on the parameterized thread delegates.
                            itemState.StartTimestamp = DateTime.UtcNow;

                            method?.Invoke(itemState.ParameterizedThreadAction, new[] { itemState.Parameter });
                        }
                    }
                    catch (Exception ex)
                    {
                        itemState.SetException(ex);
                    }

                    itemState.SetComplete(threadEnvelope.NativeThread?.TotalProcessorTime - startTotalProcessorTime);

                    tryDequeueCount = 0; //Reset the spin count if the thread dequeued an item.
                }
                else if (KeepRunning && tryDequeueCount++ >= SpinCount)
                {
                    //We have tried to dequeue too many times without success, just sleep.

                    threadEnvelope.State = PooledThreadState.Waiting;
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
