using System.Collections;
using System.Collections.Generic;
using static NTDLS.DelegateThreadPooling.DelegateThreadPool;

namespace NTDLS.DelegateThreadPooling
{
    /// <summary>
    /// Contains a collection of queue item states. Allows for determining when a set of queued items have been completed.
    /// </summary>
    /// <typeparam name="T">The type which will be passed for parameterized thread delegates.</typeparam>
    public class TrackableQueue<T> : IEnumerable<QueueItemState<T>>
    {
        /// <summary>
        /// The collection of enqueued work items and their states.
        /// </summary>
        private readonly List<QueueItemState<T>> _collection = new();
        private readonly DelegateThreadPool _threadPool;

        #region IEnumerable.

        /// <summary>
        /// Exposes the enumerator of the QueueItemState collection for iteration.
        /// </summary>
        /// <returns></returns>
        public IEnumerator<QueueItemState<T>> GetEnumerator()
        {
            return _collection.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return _collection.GetEnumerator();
        }

        #endregion

        /// <summary>
        /// The number of items in the state collection.
        /// </summary>
        public int Count
            => _collection.Count;

        /// <summary>
        /// Gets the thread state item at the give index.
        /// </summary>
        /// <param name="index"></param>
        /// <returns></returns>
        public QueueItemState<T> Item(int index)
            => _collection[index];

        internal TrackableQueue(DelegateThreadPool threadPool)
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
        public bool ExceptionOccurred()
            => _collection.Any(o => o.ExceptionOccurred);

        /// <summary>
        /// Returns a list of all items where an unhandled exception occurred.
        /// </summary>
        /// <returns></returns>
        public List<QueueItemState<T>> Exceptions()
            => _collection.Where(o => o.ExceptionOccurred).ToList();

        /// <summary>
        /// Cancels all queued worker items.
        /// </summary>
        /// <returns>Returns true if all item were cancelled.</returns>
        public bool Abort()
            => _collection.All(o => o.Abort());

        /// <summary>
        /// Blocks until all work items in the collection have been processed by a thread.
        /// </summary>
        /// <param name="suppressExceptions">When false, any exceptions will be thrown the the completion of the queue wait</param> 
        public void WaitForCompletion(bool suppressExceptions = false)
        {
            foreach (var item in _collection)
            {
                item.WaitForCompletion();
                if (_threadPool.KeepRunning == false)
                {
                    break;
                }
            }

            if (!suppressExceptions)
            {
                ThrowAnyExceptions();
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
        /// <param name="suppressExceptions">When false, any exceptions will be thrown the the completion of the queue wait</param> 
        /// <returns>Returns TRUE if all queued items completed, return FALSE on timeout.</returns>
        /// <exception cref="Exception">Exceptions are thrown if the associated thread pool is shutdown while waiting.</exception>
        public bool WaitForCompletion(int maxMillisecondsToWait, bool suppressExceptions = false)
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

            if (!suppressExceptions)
            {
                ThrowAnyExceptions();
            }

            if (_threadPool.KeepRunning == false)
            {
                throw new DelegateThreadPoolShuttingDown("The thread pool is shutting down.");
            }

            return true;
        }

        /// <summary>
        /// Throws an aggregate exception for any and all exceptions which occurred in the queue.
        /// </summary>
        /// <exception cref="AggregateException"></exception>
        public void ThrowAnyExceptions()
        {
            var exceptions = new List<Exception>();
            foreach (var item in _collection.Where(o => o.ExceptionOccurred))
            {
                if (item.Exception != null)
                {
                    exceptions.Add(item.Exception);
                }
            }
            if (exceptions.Any())
            {
                throw new AggregateException(exceptions);
            }
        }

        /// <summary>
        /// Blocks until all work items in the collection have been processed by a thread.
        /// Periodically calls the callback so that the caller can report progress.
        /// </summary>
        /// <param name="millisecondsUntilUpdate">The number of milliseconds to wait between calls to the provided periodicUpdateAction().</param>
        /// <param name="periodicUpdateAction">The delegate function to call every n-milliseconds</param>
        /// <param name="suppressExceptions">When false, any exceptions will be thrown the the completion of the queue wait</param>
        /// <exception cref="Exception"></exception>
        public bool WaitForCompletion(int millisecondsUntilUpdate, PeriodicUpdateAction periodicUpdateAction, bool suppressExceptions = false)
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

            if (!suppressExceptions)
            {
                ThrowAnyExceptions();
            }

            if (_threadPool.KeepRunning == false)
            {
                throw new DelegateThreadPoolShuttingDown("The thread pool is shutting down.");
            }

            return true;
        }
    }
}
