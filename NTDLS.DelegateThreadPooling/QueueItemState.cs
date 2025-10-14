using static NTDLS.DelegateThreadPooling.DelegateThreadPool;

namespace NTDLS.DelegateThreadPooling
{
    /// <summary>
    /// Contains information to track the state of an enqueued worker item and allows for waiting on it to complete.
    /// </summary>
    /// <typeparam name="T">The type which will be passed for parameterized thread delegates.</typeparam>
    public class QueueItemState<T> : IQueueItemState
    {
        /// <summary>
        /// A name that can be assigned to the thread state object for tracking by the user.
        /// </summary>
        public string? Name { get; set; }

        /// <summary>
        /// The UTC date/time which the thread worker started.
        /// </summary>
        public DateTime StartTimestamp { get; set; }

        /// <summary>
        /// The duration of the threads delegate operation.
        /// </summary>
        public TimeSpan? CompletionTime { get; private set; }

        /// <summary>
        /// The amount of CPU time consumed by the threads delegate operation.
        /// </summary>
        public TimeSpan? ProcessorTime { get; set; }

        /// <summary>
        /// Delegate which is called once the thread completes.
        /// </summary>
        public ThreadCompleteActionDelegate<T>? ThreadCompleteAction { get; private set; }

        /// <summary>
        /// Non-parameterized thread worker delegate.
        /// </summary>
        public ThreadActionDelegate? ThreadAction { get; private set; }

        /// <summary>
        /// Parameterized thread worker delegate.
        /// </summary>
        public ParameterizedThreadActionDelegate<T>? ParameterizedThreadAction { get; private set; }

        #region IQueueItemState.

        ParameterizedThreadActionDelegate<object>? IQueueItemState.ParameterizedThreadAction
            => ParameterizedThreadAction != null ? new ParameterizedThreadActionDelegate<object>(o => ParameterizedThreadAction((T)o)) : null;

        ThreadCompleteActionDelegate<object>? IQueueItemState.ThreadCompleteAction
            => ThreadCompleteAction != null ? new ThreadCompleteActionDelegate<object>(o => ThreadCompleteAction(this)) : null;

        #endregion

        /// <summary>
        /// Thread pool which owns the item state.
        /// </summary>
        public DelegateThreadPool OwnerThreadPool { get; private set; }

        /// <summary>
        /// The user-settable parameter that will be passed to the delegate function.
        /// </summary>
        public object? Parameter { get; set; }

        /// <summary>
        /// Denotes if the queued work item has been completed.
        /// </summary>
        public bool IsComplete { get; private set; }

        /// <summary>
        /// Denotes if the queued work item was aborted before it was finished.
        /// </summary>
        public bool WasAborted { get; private set; }

        /// <summary>
        /// Is set to true if an exception occurred when executing the delegate command. Check Exception for details.
        /// </summary>
        public bool ExceptionOccurred { get; private set; }

        /// <summary>
        /// Is set if an exception occurred when executing the delegate command, otherwise null.
        /// </summary>
        public Exception? Exception { get; private set; }

        internal QueueItemState(DelegateThreadPool ownerThreadPool, ThreadActionDelegate threadAction, ThreadCompleteActionDelegate<T>? onComplete = null)
        {
            Parameter = null;
            OwnerThreadPool = ownerThreadPool;
            ThreadAction = threadAction;
            ThreadCompleteAction = onComplete;
        }

        internal QueueItemState(DelegateThreadPool ownerThreadPool, object? parameter, ParameterizedThreadActionDelegate<T> parameterizedThreadAction, ThreadCompleteActionDelegate<T>? onComplete = null)
        {
            Parameter = parameter;
            OwnerThreadPool = ownerThreadPool;
            ParameterizedThreadAction = parameterizedThreadAction;
            ThreadCompleteAction = onComplete;
        }

        /// <summary>
        /// Sets the thread state as complete.
        /// </summary>
        public void SetComplete(TimeSpan? processorTime)
        {
            CompletionTime = DateTime.UtcNow - StartTimestamp;
            ProcessorTime = processorTime;

            IsComplete = true;
            OwnerThreadPool.QueueItemStateCompletion.Set();
            ThreadCompleteAction?.Invoke(this);
        }

        /// <summary>
        /// Sets the thread exception state as complete.
        /// </summary>
        public void SetException(Exception ex)
        {
            CompletionTime = DateTime.UtcNow - StartTimestamp;
            Exception = ex;
            ExceptionOccurred = true;
        }

        /// <summary>
        /// Cancels the queued worker item. 
        /// </summary>
        /// <returns>Returns true if the item was cancelled.</returns>
        public bool Abort()
        {
            if (IsComplete == false)
            {
                WasAborted = true;
                SetComplete(TimeSpan.Zero);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Blocks until the work item has been processed by a thread in the pool.
        /// </summary>
        /// <param name="maxMillisecondsToWait">The maximum number of milliseconds to wait for the queue item work to complete.</param>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        public bool WaitForCompletion(int maxMillisecondsToWait)
        {
            var startTime = DateTime.UtcNow;

            uint tryCount = 0;
            while (OwnerThreadPool.KeepThreadPoolRunning && IsComplete == false)
            {
                if (tryCount++ == OwnerThreadPool.Configuration.SpinCount)
                {
                    tryCount = 0;
                    OwnerThreadPool.QueueItemStateCompletion.WaitOne(OwnerThreadPool.Configuration.WaitDuration);

                    if ((DateTime.UtcNow - startTime).TotalMilliseconds > maxMillisecondsToWait)
                    {
                        return false;
                    }
                }
            }

            if (OwnerThreadPool.KeepThreadPoolRunning == false)
            {
                throw new DelegateThreadPoolShuttingDown("The thread pool is shutting down.");
            }

            return true;
        }

        /// <summary>
        /// Blocks until the work item has been processed by a thread in the pool.
        /// </summary>
        /// <returns></returns>
        public void WaitForCompletion()
        {
            uint tryCount = 0;
            while (OwnerThreadPool.KeepThreadPoolRunning && IsComplete == false)
            {
                if (tryCount++ == OwnerThreadPool.Configuration.SpinCount)
                {
                    tryCount = 0;
                    OwnerThreadPool.QueueItemStateCompletion.WaitOne(OwnerThreadPool.Configuration.WaitDuration);
                }
            }

            if (OwnerThreadPool.KeepThreadPoolRunning == false)
            {
                throw new DelegateThreadPoolShuttingDown("The thread pool is shutting down.");
            }
        }

        /// <summary>
        /// Blocks until the work item has been processed by a thread in the pool.
        /// </summary>
        /// <param name="updateDelay">The amount of time to wait between calls to the provided periodicUpdateAction().</param>
        /// <param name="periodicUpdateAction">The delegate function to call every n-milliseconds</param>
        /// <returns></returns>
        public bool WaitForCompletion(TimeSpan updateDelay, PeriodicUpdateActionDelegate periodicUpdateAction)
        {
            var lastUpdate = DateTime.UtcNow;

            uint tryCount = 0;
            while (OwnerThreadPool.KeepThreadPoolRunning && IsComplete == false)
            {
                if (tryCount++ == OwnerThreadPool.Configuration.SpinCount)
                {
                    tryCount = 0;
                    OwnerThreadPool.QueueItemStateCompletion.WaitOne(OwnerThreadPool.Configuration.WaitDuration);

                    if ((DateTime.UtcNow - lastUpdate).TotalMilliseconds > updateDelay.TotalMilliseconds)
                    {
                        if (periodicUpdateAction() == false)
                        {
                            return false;
                        }
                        lastUpdate = DateTime.UtcNow;
                    }
                }
            }

            if (OwnerThreadPool.KeepThreadPoolRunning == false)
            {
                throw new DelegateThreadPoolShuttingDown("The thread pool is shutting down.");
            }
            return true;
        }
    }
}
