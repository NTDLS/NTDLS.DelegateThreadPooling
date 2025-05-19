using System.Diagnostics;

namespace NTDLS.DelegateThreadPooling
{
    /// <summary>
    /// Worker thread envelope used for managing and interrogating thread states.
    /// </summary>
    public class PooledThreadEnvelope
    {
        /// <summary>
        /// State of thread.
        /// </summary>
        public enum PooledThreadState
        {
            /// <summary>
            /// The thread is currently waiting on an item to deque.
            /// </summary>
            Waiting,
            /// <summary>
            /// Thread is executing. 
            /// </summary>
            Executing
        }

        /// <summary>
        /// Managed thread which is used to execute workloads.
        /// </summary>
        public Thread ManagedThread { get; private set; }

        /// <summary>
        /// Whether the thread should remain running.
        /// </summary>
        internal bool KeepThreadRunning { get; set; }

        /// <summary>
        /// Native process thread which is used to execute workloads.
        /// </summary>
        public ProcessThread? NativeThread { get; internal set; }

        /// <summary>
        /// Wait event that is used to signal the worker thread to deque items.
        /// </summary>
        public AutoResetEvent WaitEvent { get; private set; } = new(true);

        /// <summary>
        /// Current execution state of the thread.
        /// </summary>
        public PooledThreadState State { get; set; }

        /// <summary>
        /// Tell the thread to check the queue.
        /// </summary>
        internal void Signal() => WaitEvent.Set();

        /// <summary>
        /// Wait for small timeout or dequeue event.
        /// </summary>
        /// <returns></returns>
        internal bool Wait() => WaitEvent.WaitOne();

        /// <summary>
        /// Join the thread, used to wait for termination.
        /// </summary>
        internal void Join() => ManagedThread.Join();

        /// <summary>
        /// Used to tell the thread to stop at its next earliest convenience.
        /// </summary>
        internal void Shutdown()
        {
            KeepThreadRunning = false;
            Signal();
        }

        internal PooledThreadEnvelope(DelegateThreadPoolConfiguration configuration, ParameterizedThreadStart proc)
        {
            State = PooledThreadState.Waiting;
            ManagedThread = new Thread(proc)
            {
                IsBackground = configuration.UseBackgroundThreads,
                Priority = configuration.WorkerPriority
            };

            ManagedThread.Start(this);
        }
    }
}
