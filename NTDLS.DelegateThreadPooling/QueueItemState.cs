using static NTDLS.DelegateThreadPooling.DelegateThreadPool;

namespace NTDLS.DelegateThreadPooling
{
    /// <summary>
    /// Contains information to track the state of an enqueued worker item and allows for waiting on it to complete.
    /// </summary>
    public class QueueItemState<T> : IQueueItemState
    {
        private readonly AutoResetEvent _queueWaitEvent = new(false);

        /// <summary>
        /// A name that can be assigned to the thread state object for tracking by the user.
        /// </summary>
        public string? Name { get; set; }

        /// <summary>
        /// The UTC date/time that the thread started.
        /// </summary>
        public DateTime StartTimestamp { get; private set; }

        /// <summary>
        /// The duration of the threads delegate operation.
        /// </summary>
        public TimeSpan? CompletionTime { get; private set; }

        /// <summary>
        /// Delegate which is called once the thread completes.
        /// </summary>
        public ThreadCompleteAction? OnComplete { get; private set; }

        /// <summary>
        /// Non-parameterized thread worker delegate.
        /// </summary>
        public ThreadAction? ThreadAction { get; private set; }

        /// <summary>
        /// Parameterized thread worker delegate.
        /// </summary>
        public ParameterizedThreadAction<T>? ParameterizedThreadAction { get; private set; }
        ParameterizedThreadAction<object>? IQueueItemState.ParameterizedThreadAction
            => ParameterizedThreadAction != null ? new ParameterizedThreadAction<object>(o => ParameterizedThreadAction((T?)o)) : null;

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

        internal QueueItemState(DelegateThreadPool ownerThreadPool, ThreadAction threadAction, ThreadCompleteAction? onComplete = null)
        {
            StartTimestamp = DateTime.UtcNow;
            Parameter = null;
            OwnerThreadPool = ownerThreadPool;
            ThreadAction = threadAction;
            OnComplete = onComplete;
        }

        internal QueueItemState(DelegateThreadPool ownerThreadPool, object? parameter, ParameterizedThreadAction<T> parameterizedThreadAction, ThreadCompleteAction? onComplete = null)
        {
            StartTimestamp = DateTime.UtcNow;
            Parameter = parameter;
            OwnerThreadPool = ownerThreadPool;
            ParameterizedThreadAction = parameterizedThreadAction;
            OnComplete = onComplete;
        }

        /// <summary>
        /// Sets the thread state as complete.
        /// </summary>
        public void SetComplete()
        {
            CompletionTime = DateTime.UtcNow - StartTimestamp;

            IsComplete = true;
            _queueWaitEvent.Set();
            OnComplete?.Invoke();
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
                SetComplete();
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
            while (OwnerThreadPool.KeepRunning && IsComplete == false)
            {
                if (tryCount++ == OwnerThreadPool.SpinCount)
                {
                    tryCount = 0;
                    _queueWaitEvent.WaitOne(OwnerThreadPool.WaitDuration);

                    if ((DateTime.UtcNow - startTime).TotalMilliseconds > maxMillisecondsToWait)
                    {
                        return false;
                    }
                }
            }

            if (OwnerThreadPool.KeepRunning == false)
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
            while (OwnerThreadPool.KeepRunning && IsComplete == false)
            {
                if (tryCount++ == OwnerThreadPool.SpinCount)
                {
                    tryCount = 0;
                    _queueWaitEvent.WaitOne(OwnerThreadPool.WaitDuration);
                }
            }

            if (OwnerThreadPool.KeepRunning == false)
            {
                throw new DelegateThreadPoolShuttingDown("The thread pool is shutting down.");
            }
        }

        /// <summary>
        /// Blocks until the work item has been processed by a thread in the pool.
        /// </summary>
        /// <param name="millisecondsUntilUpdate">The number of milliseconds to wait between calls to the provided periodicUpdateAction().</param>
        /// <param name="periodicUpdateAction">The delegate function to call every n-milliseconds</param>
        /// <returns></returns>
        public bool WaitForCompletion(int millisecondsUntilUpdate, PeriodicUpdateAction periodicUpdateAction)
        {
            var lastUpdate = DateTime.UtcNow;

            uint tryCount = 0;
            while (OwnerThreadPool.KeepRunning && IsComplete == false)
            {
                if (tryCount++ == OwnerThreadPool.SpinCount)
                {
                    tryCount = 0;
                    _queueWaitEvent.WaitOne(OwnerThreadPool.WaitDuration);

                    if ((DateTime.UtcNow - lastUpdate).TotalMilliseconds > millisecondsUntilUpdate)
                    {
                        if (periodicUpdateAction() == false)
                        {
                            return false;
                        }
                        lastUpdate = DateTime.UtcNow;
                    }
                }
            }

            if (OwnerThreadPool.KeepRunning == false)
            {
                throw new DelegateThreadPoolShuttingDown("The thread pool is shutting down.");
            }
            return true;
        }
    }
}
