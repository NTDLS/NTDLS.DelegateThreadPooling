namespace NTDLS.DelegateThreadPooling
{
    /// <summary>
    /// General configuration for the delegate thread pool.
    /// </summary>
    public class DelegateThreadPoolConfiguration
    {
        /// <summary>
        /// Initial number of threads to spawn as instantiation.
        /// </summary>
        public int InitialThreadCount { get; set; } = Environment.ProcessorCount;

        /// <summary>
        /// The maximum number of threads to spawn upon thread pool exhaustion.
        /// </summary>
        public int MaximumThreadCount { get; set; } = Environment.ProcessorCount * 4;

        /// <summary>
        /// Priority of the worker threads in the pool. Use anything above Normal with extreme caution.
        /// </summary>
        public ThreadPriority WorkerPriority { get; set; } = ThreadPriority.Normal;

        /// <summary>
        /// Whether or not to use background threads for the workers.
        /// </summary>
        public bool UseBackgroundThreads { get; set; } = true;

        /// <summary>
        /// The maximum number of items that can be queued before blocking. Zero indicates unlimited.
        /// </summary>
        public int MaximumQueueDepth { get; set; } = 0;

        /// <summary>
        /// The number of times to repeatedly check the internal lock availability before going to going to sleep.
        /// </summary>
        public int SpinCount { get; set; } = 100;

        /// <summary>
        /// The number of milliseconds to wait for queued items after each expiration of SpinCount.
        /// </summary>
        public int WaitDuration { get; set; } = 1;

        /// <summary>
        /// The low-end-range of time to allow the queue to be overloaded before growing the thread-pool.
        /// </summary>
        public int AutoGrowthMinimumOverloadThresholdMs = 100;

        /// <summary>
        /// The multiplier for the Auto-Growth Overload Threshold.
        /// Each time the thread-pool is grown, the threshold milliseconds is multiplied by this value.
        /// Once overload conditions flatten, the threshold is reduced back to the minimum value.
        /// </summary>
        public int AutoGrowthOverloadDurationGrowthFactor = 2;

        /// <summary>
        /// The high-end-range of time to allow the queue to be overloaded before growing the thread-pool.
        /// </summary>
        public int AutoGrowthMaximumOverloadThresholdMs = 6400;

        /// <summary>
        /// The amount of time that the queue must be idle before reducing the number of threads in the pool
        /// </summary>
        public int AutoShrinkUnderloadThresholdMs { get; set; } = 30000;
    }
}
