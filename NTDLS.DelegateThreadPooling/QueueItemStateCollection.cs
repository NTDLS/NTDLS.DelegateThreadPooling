﻿using static NTDLS.DelegateThreadPooling.DelegateThreadPool;

namespace NTDLS.DelegateThreadPooling
{
    /// <summary>
    /// Contains a collection of queue tokens. Allows for determining when a set of queued items have been completed.
    /// </summary>
    public class QueueItemStateCollection
    {
        /// <summary>
        /// The collection of enqueued work items and their states.
        /// </summary>
        private readonly List<QueueItemState> _collection = new();
        private readonly DelegateThreadPool _threadPool;

        /// <summary>
        /// The number of items in the state collection.
        /// </summary>
        public int Count => _collection.Count;

        internal QueueItemStateCollection(DelegateThreadPool threadPool)
        {
            _threadPool = threadPool;
        }

        /// <summary>
        /// Adds a delegate function to the work queue.
        /// </summary>
        /// <param name="threadAction">Returns a token that allows for waiting on the queued item.</param>
        /// <returns></returns>
        public QueueItemState Enqueue(ThreadAction threadAction)
        {
            _collection.RemoveAll(o => o.IsComplete == true && o.ExceptionOccured == false);

            var queueToken = _threadPool.Enqueue(threadAction);
            _collection.Add(queueToken);
            return queueToken;
        }

        /// <summary>
        /// Adds a delegate function to the work queue.
        /// </summary>
        /// <param name="parameter">User supplied parameter that will be passed to the delegate function.</param>
        /// <param name="parameterizedThreadAction">The delegate function to execute when a thread is ready.</param>
        /// <returns></returns>
        public QueueItemState Enqueue(object parameter, ParameterizedThreadAction parameterizedThreadAction)
        {
            _collection.RemoveAll(o => o.IsComplete == true && o.ExceptionOccured == false);

            var queueToken = _threadPool.Enqueue(parameter, parameterizedThreadAction);
            _collection.Add(queueToken);
            return queueToken;
        }

        /// <summary>
        /// Returns true is any of the items have an exception.
        /// </summary>
        /// <returns></returns>
        public bool ExceptionOccured()
        {
            return _collection.Any(o => o.ExceptionOccured);
        }

        /// <summary>
        /// Cancels all queued worker items.
        /// </summary>
        /// <returns>Returns true if all item were cancelled.</returns>
        public bool Abort()
        {
            return _collection.All(o => o.Abort());
        }

        /// <summary>
        /// Blocks until all work items in the collection have been processed by a thread.
        /// </summary>
        public void WaitForCompletion()
        {
            while (_threadPool.KeepRunning && _collection.All(o => o.WaitForCompletion()) == false)
            {
                Thread.Yield();
            }

            if (_threadPool.KeepRunning == false)
            {
                throw new Exception("The thread pool is shutting down.");
            }
        }
    }
}
