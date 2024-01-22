namespace NTDLS.DelegateThreadPool
{
    /// <summary>
    /// Contains information to track the state of an enqueued worker item and allows for waiting on it to complete.
    /// </summary>
    public class QueueItemState
    {
        private readonly AutoResetEvent _queueWaitEvent = new(false);

        internal DelegateThreadPool.ThreadAction ThreadAction { get; private set; }

        internal DelegateThreadPool OwnerThreadPool { get; private set; }

        /// <summary>
        /// Denotes if the queued work item has been completed.
        /// </summary>
        public bool IsComplete { get; private set; }


        /// <summary>
        /// Is set to true if an exception occured when executing the delegate command. Check Exception for details.
        /// </summary>
        public bool ExceptionOccured { get; private set; }

        /// <summary>
        /// Is set if an exception occured when executing the delegate command, otherwise null.
        /// </summary>
        public Exception? Exception { get; private set; }

        internal QueueItemState(DelegateThreadPool ownerThreadPool, DelegateThreadPool.ThreadAction threadAction)
        {
            OwnerThreadPool = ownerThreadPool;
            ThreadAction = threadAction;
        }

        internal void SetComplete()
        {
            IsComplete = true;
            _queueWaitEvent.Set();
        }

        internal void SetException(Exception ex)
        {
            Exception = ex;
            ExceptionOccured = true;
        }

        /// <summary>
        /// Cancels the queued worker item. 
        /// </summary>
        /// <returns>Returns true if the item was cancelled.</returns>
        public bool Abort()
        {
            if (IsComplete == false)
            {
                SetComplete();
                return true;
            }
            return false;
        }

        /// <summary>
        /// Blocks until the work item has been processed by a thread in the pool.
        /// </summary>
        /// <returns></returns>
        public bool WaitForCompletion()
        {
            int tries = 0;
            while (IsComplete == false)
            {
                if ((++tries % OwnerThreadPool.SpinCount) == 0)
                {
                    _queueWaitEvent.WaitOne(OwnerThreadPool.WaitDuration);
                }
            }
            return true;
        }
    }
}
