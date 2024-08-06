using static NTDLS.DelegateThreadPooling.DelegateThreadPool;

namespace NTDLS.DelegateThreadPooling
{
    /// <summary>
    /// Contains a collection of queue item states. Allows for determining when a set of queued items have been completed.
    /// </summary>
    /// <typeparam name="T">The type which will be passed for parameterized thread delegates.</typeparam>
    public class QueueItemStates<T>
    {
        /// <summary>
        /// The collection of enqueued work items and their states.
        /// </summary>
        private readonly List<QueueItemState<T>> _collection = new();
        private readonly DelegateThreadPool _threadPool;

        /// <summary>
        /// The number of items in the state collection.
        /// </summary>
        public int Count => _collection.Count;

        /// <summary>
        /// Gets the thread state item at the give index.
        /// </summary>
        /// <param name="index"></param>
        /// <returns></returns>
        public QueueItemState<T> Item(int index) => _collection[index];

        internal QueueItemStates(DelegateThreadPool threadPool)
        {
            _threadPool = threadPool;
        }

        /// <summary>
        /// Adds a delegate function to the work queue.
        /// </summary>
        /// <param name="threadAction">Returns a token that allows for waiting on the queued item.</param>
        /// <returns></returns>
        public QueueItemState<T> Enqueue(ThreadAction threadAction)
        {
            _collection.RemoveAll(o => o.IsComplete == true && o.ExceptionOccurred == false);

            var queueToken = _threadPool.Enqueue<T>(threadAction);
            _collection.Add(queueToken);
            return queueToken;
        }

        /// <summary>
        /// Adds a delegate function to the work queue.
        /// </summary>
        /// <param name="threadAction">Returns a token that allows for waiting on the queued item.</param>
        /// <param name="onComplete">The delegate function to call when the queue item is finished processing.</param>
        /// <returns></returns>
        public QueueItemState<T> Enqueue(ThreadAction threadAction, ThreadCompleteAction onComplete)
        {
            _collection.RemoveAll(o => o.IsComplete == true && o.ExceptionOccurred == false);

            var queueToken = _threadPool.Enqueue<T>(threadAction, onComplete);
            _collection.Add(queueToken);
            return queueToken;
        }

        /// <summary>
        /// Adds a delegate function to the work queue.
        /// </summary>
        /// <param name="parameter">User supplied parameter that will be passed to the delegate function.</param>
        /// <param name="parameterizedThreadAction">The delegate function to execute when a thread is ready.</param>
        /// <param name="onComplete">The delegate function to call when the queue item is finished processing.</param>
        /// <returns></returns>
        public QueueItemState<T> Enqueue(T parameter, ParameterizedThreadAction<T> parameterizedThreadAction, ThreadCompleteAction onComplete)
        {
            _collection.RemoveAll(o => o.IsComplete == true && o.ExceptionOccurred == false);

            var queueToken = _threadPool.Enqueue(parameter, parameterizedThreadAction, onComplete);
            _collection.Add(queueToken);
            return queueToken;
        }

        /// <summary>
        /// Adds a delegate function to the work queue.
        /// </summary>
        /// <param name="parameter">User supplied parameter that will be passed to the delegate function.</param>
        /// <param name="parameterizedThreadAction">The delegate function to execute when a thread is ready.</param>
        /// <returns></returns>
        public QueueItemState<T> Enqueue(T parameter, ParameterizedThreadAction<T> parameterizedThreadAction)
        {
            _collection.RemoveAll(o => o.IsComplete == true && o.ExceptionOccurred == false);

            var queueToken = _threadPool.Enqueue(parameter, parameterizedThreadAction);
            _collection.Add(queueToken);
            return queueToken;
        }

        /// <summary>
        /// Returns true is any of the items have an exception.
        /// </summary>
        /// <returns></returns>
        public bool ExceptionOccurred() => _collection.Any(o => o.ExceptionOccurred);

        /// <summary>
        /// Cancels all queued worker items.
        /// </summary>
        /// <returns>Returns true if all item were cancelled.</returns>
        public bool Abort() => _collection.All(o => o.Abort());

        /// <summary>
        /// Blocks until all work items in the collection have been processed by a thread.
        /// </summary>
        public void WaitForCompletion()
        {
            foreach (var item in _collection)
            {
                item.WaitForCompletion();
                if (_threadPool.KeepRunning == false)
                {
                    break;
                }
            }

            if (_threadPool.KeepRunning == false)
            {
                throw new DelegateThreadPoolShuttingDown("The thread pool is shutting down.");
            }
        }

        /// <summary>
        /// Blocks until all work items in the collection have been processed by a thread or
        /// the timeout expires. The timeout expiring does not cancel the queued work items.
        /// </summary>
        /// <param name="maxMillisecondsToWait"></param>
        /// <returns>Returns TRUE if all queued items completed, return FALSE on timeout.</returns>
        /// <exception cref="Exception">Exceptions are thrown if the associated thread pool is shutdown while waiting.</exception>
        public bool WaitForCompletion(int maxMillisecondsToWait)
        {
            var startTime = DateTime.UtcNow;

            foreach (var item in _collection)
            {
                if (item.WaitForCompletion(maxMillisecondsToWait) == false)
                {
                    return false;
                }

                if ((DateTime.UtcNow - startTime).TotalMilliseconds > maxMillisecondsToWait)
                {
                    return false;
                }

                if (_threadPool.KeepRunning == false)
                {
                    break;
                }
            }

            if (_threadPool.KeepRunning == false)
            {
                throw new DelegateThreadPoolShuttingDown("The thread pool is shutting down.");
            }

            return true;
        }

        /// <summary>
        /// Blocks until all work items in the collection have been processed by a thread.
        /// Periodically calls the callback so that the caller can report progress.
        /// </summary>
        /// <param name="millisecondsUntilUpdate">The number of milliseconds to wait between calls to the provided periodicUpdateAction().</param>
        /// <param name="periodicUpdateAction">The delegate function to call every n-milliseconds</param>
        /// <exception cref="Exception"></exception>
        public bool WaitForCompletion(int millisecondsUntilUpdate, PeriodicUpdateAction periodicUpdateAction)
        {
            var lastUpdate = DateTime.UtcNow;

            foreach (var item in _collection)
            {
                if (item.WaitForCompletion(millisecondsUntilUpdate, periodicUpdateAction) == false)
                {
                    return false;
                }

                if ((DateTime.UtcNow - lastUpdate).TotalMilliseconds > millisecondsUntilUpdate)
                {
                    periodicUpdateAction();
                    lastUpdate = DateTime.UtcNow;
                }
            }

            if (_threadPool.KeepRunning == false)
            {
                throw new DelegateThreadPoolShuttingDown("The thread pool is shutting down.");
            }

            return true;
        }
    }
}
