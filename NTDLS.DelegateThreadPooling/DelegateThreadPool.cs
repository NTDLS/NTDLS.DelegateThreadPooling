using NTDLS.Semaphore;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using static NTDLS.DelegateThreadPooling.PooledThreadEnvelope;

namespace NTDLS.DelegateThreadPooling
{
    /// <summary>
    /// Creates an active thread pool where work items can be queued as delegate functions.
    /// </summary>
    public class DelegateThreadPool : IDisposable
    {
        /// <summary>
        /// Used for the QueueItemStates to signal their completion.
        /// This is not used at the DelegateThreadPool level, but is shared across all
        /// QueueItemStates to reduce handle count at the slight tradeoff to extra completion checks.
        /// </summary>
        internal readonly AutoResetEvent QueueItemStateCompletion = new(false);
        internal readonly DelegateThreadPoolConfiguration Configuration;
        internal readonly AutoResetEvent ItemDequeuedWaitEvent = new(true);

        internal bool KeepThreadPoolRunning { get; private set; } = false;

        private static readonly ConcurrentDictionary<Type, MethodInfo> _reflectionCache = new();
        private readonly Timer _growthMonitorTimer;
        private DateTime? _lastOverloadedTime = null;
        private DateTime? _lastUnderloadTime = null;
        private int? _autoGrowthOverloadThresholdMs = null;
        private readonly PessimisticCriticalResource<List<PooledThreadEnvelope>> _threadEnvelopes = new();
        private readonly PessimisticCriticalResource<Queue<IQueueItemState>> _actions = new();

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

        /// <summary>
        /// Provides read-only access to threads in the thread pool for diagnostics and performance reporting.
        /// </summary>
        public IReadOnlyList<PooledThreadEnvelope> Threads { get => _threadEnvelopes.Use(o => o); }

        /// <summary>
        /// The current number of threads in the thread pool.
        /// </summary>
        public int ThreadCount { get => _threadEnvelopes.Use(o => o.Count); }


        /// <summary>
        /// Starts n worker threads, where n is the number of CPU cores available to the operating system.
        /// </summary>
        public DelegateThreadPool()
        {
            KeepThreadPoolRunning = true;

            Configuration = new DelegateThreadPoolConfiguration();
            _threadEnvelopes.Use(o =>
            {
                while (o.Count < Configuration.InitialThreadCount)
                {
                    o.Add(new PooledThreadEnvelope(Configuration, InternalThreadProc));
                }
            });

            _growthMonitorTimer = new Timer(AutoGrowthMonitorCallback, null, TimeSpan.Zero, TimeSpan.FromMilliseconds(100));
        }

        /// <summary>
        /// Defines the pool size and starts the worker threads.
        /// </summary>
        /// <param name="configuration">General configuration for the delegate thread pool.</param>
        public DelegateThreadPool(DelegateThreadPoolConfiguration configuration)
        {
            Configuration = configuration;

            if (Configuration.MaximumQueueDepth < 0)
            {
                throw new ArgumentOutOfRangeException("MaximumQueueDepth must be equal or greater than 0.");
            }

            if (Configuration.MaximumThreadCount < configuration.InitialThreadCount)
            {
                throw new ArgumentOutOfRangeException("MaximumThreadCount must be equal or greater than InitialThreadCount.");
            }

            KeepThreadPoolRunning = true;
            _threadEnvelopes.Use(o =>
            {
                while (o.Count < Configuration.InitialThreadCount)
                {
                    o.Add(new PooledThreadEnvelope(Configuration, InternalThreadProc));
                }
            });

            _growthMonitorTimer = new Timer(AutoGrowthMonitorCallback, null, TimeSpan.Zero, TimeSpan.FromMilliseconds(100));
        }

        private void AutoGrowthMonitorCallback(object? state)
        {
            _autoGrowthOverloadThresholdMs ??= Configuration.AutoGrowthMinimumOverloadThresholdMs;

            bool hasIdleThreads = _threadEnvelopes.Use(o => o.Any(o => o.State == PooledThreadState.Waiting));

            int queueDepth = _actions.Use(q => q.Count);

            if (ThreadCount < Configuration.MaximumThreadCount && !hasIdleThreads && queueDepth >= ThreadCount)
            {
                _lastOverloadedTime ??= DateTime.UtcNow;

                if ((DateTime.UtcNow - _lastOverloadedTime.Value).TotalMilliseconds > _autoGrowthOverloadThresholdMs)
                {
                    _lastOverloadedTime = null;

                    if (ThreadCount < Configuration.MaximumThreadCount)
                    {
                        _threadEnvelopes.Use(o => o.Add(new PooledThreadEnvelope(Configuration, InternalThreadProc)));

                        // Increase threshold for next time (with cap)
                        _autoGrowthOverloadThresholdMs = Math.Min(
                            _autoGrowthOverloadThresholdMs.Value * Configuration.AutoGrowthOverloadDurationGrowthFactor,
                            Configuration.AutoGrowthMaximumOverloadThresholdMs
                        );
                    }
                }
            }
            else if (_lastOverloadedTime != null)
            {
                // Reset on stability.
                _lastOverloadedTime = null;
                _autoGrowthOverloadThresholdMs = Configuration.AutoGrowthMinimumOverloadThresholdMs;
            }

            //Auto-shrink:
            // --- Auto Shrink ---
            if (ThreadCount > Configuration.InitialThreadCount && hasIdleThreads && queueDepth == 0)
            {
                _lastUnderloadTime ??= DateTime.UtcNow;

                if ((DateTime.UtcNow - _lastUnderloadTime.Value).TotalMilliseconds > Configuration.AutoShrinkUnderloadThresholdMs)
                {
                    _lastUnderloadTime = null;

                    _threadEnvelopes.Use(o =>
                    {
                        var idleThread = o.LastOrDefault(t => t.State == PooledThreadState.Waiting);
                        if (idleThread != null)
                        {
                            idleThread.Shutdown();
                            o.Remove(idleThread);
                        }
                    });
                }
            }
            else
            {
                _lastUnderloadTime = null;
            }
        }

        /// <summary>
        /// Creates a QueueItemState collection. This allows you to keep track of a set
        /// of items that have been queued so that you can wait on them to complete.
        /// </summary>
        public DelegateThreadChildPool CreateChildPool(int maxChildQueueDepth = 0)
        {
            return new DelegateThreadChildPool(this, maxChildQueueDepth);
        }

        /// <summary>
        /// Creates a QueueItemState collection. This allows you to keep track of a set
        /// of items that have been queued so that you can wait on them to complete.
        /// </summary>
        /// <typeparam name="T">The type which will be passed to the parameterized thread delegate.</typeparam>
        public DelegateThreadChildPool<T> CreateChildPool<T>(int maxChildQueueDepth = 0)
        {
            return new DelegateThreadChildPool<T>(this, maxChildQueueDepth);
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
            if (Configuration.MaximumQueueDepth > 0)
            {
                uint tryCount = 0;

                while (KeepThreadPoolRunning)
                {
                    if (_actions.Use(o => o.Count) < Configuration.MaximumQueueDepth)
                    {
                        break;
                    }

                    if (tryCount++ == Configuration.SpinCount)
                    {
                        tryCount = 0;
                        //Wait for a small amount of time or until the event is signaled (which 
                        //indicates that an item has been dequeued thereby creating free space).
                        ItemDequeuedWaitEvent.WaitOne(Configuration.WaitDuration);
                    }
                }

                if (KeepThreadPoolRunning == false)
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
            if (Configuration.MaximumQueueDepth > 0)
            {
                uint tryCount = 0;

                while (KeepThreadPoolRunning)
                {
                    int queueSize = _actions.Use(o => o.Count);
                    if (queueSize < Configuration.MaximumQueueDepth)
                    {
                        break;
                    }

                    if (tryCount++ == Configuration.SpinCount)
                    {
                        tryCount = 0;
                        //Wait for a small amount of time or until the event is signaled (which 
                        //indicates that an item has been dequeued thereby creating free space).
                        ItemDequeuedWaitEvent.WaitOne(Configuration.WaitDuration);
                    }
                }

                if (KeepThreadPoolRunning == false)
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
            _threadEnvelopes.Use(o => o.FirstOrDefault(o => o.State == PooledThreadState.Waiting))?.Signal();
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
            if (KeepThreadPoolRunning)
            {
                KeepThreadPoolRunning = false;

                _growthMonitorTimer.Dispose();

                _threadEnvelopes.Use(envelopes =>
                {
                    envelopes.ForEach(o =>
                    {
                        o.Signal();
                        o.Join();
                    });
                    envelopes.Clear();
                });
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

            threadEnvelope.KeepThreadRunning = true;

#if DEBUG
            Thread.CurrentThread.Name = $"InternalThreadProc_{threadEnvelope.ManagedThread.ManagedThreadId}";
#endif

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
            while (KeepThreadPoolRunning && threadEnvelope.KeepThreadRunning)
            {
                var itemState = _actions.Use(o =>
                {
                    if (o.TryDequeue(out var dequeued))
                    {
                        //Enqueue might be blocking due to enforcing max queue depth size, tell it that the queue depth has decreased.
                        ItemDequeuedWaitEvent.Set();
                    }
                    if (dequeued?.IsComplete == true)
                    {
                        //Queued items where IsComplete == true have been aborted, just ignore them.
                        return null;
                    }
                    return dequeued;
                });

                //We do not check KeepThreadRunning here, because if we dequeued an item then
                //  we need to process it even if the individual worker is going to shutdown.
                if (KeepThreadPoolRunning && itemState != null)
                {
                    threadEnvelope.State = PooledThreadState.Executing;

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
                    finally
                    {
                        //The thread has finished working on the dequeued item.

                        itemState.SetComplete(threadEnvelope.NativeThread?.TotalProcessorTime - startTotalProcessorTime);

                        threadEnvelope.State = PooledThreadState.Waiting;
                        tryDequeueCount = 0; //Reset the spin count if the thread dequeued an item.
                    }
                }
                else if (KeepThreadPoolRunning && tryDequeueCount++ >= Configuration.SpinCount)
                {
                    //We have tried to dequeue too many times without success, just sleep.
                    if (threadEnvelope.Wait())
                    {
                        tryDequeueCount = 0; //Reset the spin count if the thread was signaled.
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
