namespace NTDLS.DelegateThreadPool
{
    /// <summary>
    /// Contains information to track the state of an enqueued worker item and allows for waiting on it to complete.
    /// </summary>
    public class QueueItemState
    {
        private readonly AutoResetEvent _queueWaitEvent = new(false);

        internal DelegateThreadPool.ThreadAction ThreadAction { get;  set; }

        /// <summary>
        /// Denotes if the queued work item has been completed.
        /// </summary>
        public bool IsComplete { get; private set; }


        /// <summary>
        /// 
        /// </summary>
        /// <param name="threadAction"></param>
        public QueueItemState(DelegateThreadPool.ThreadAction threadAction)
        {
            ThreadAction = threadAction;
        }

        internal void SetComplete()
        {
            IsComplete = true;
            _queueWaitEvent.Set();
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
                if ((++tries % 10000) == 0)
                {
                    _queueWaitEvent.WaitOne(1);
                }
            }
            return true;
        }
    }
}
