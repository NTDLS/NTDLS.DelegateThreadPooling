using static NTDLS.DelegateThreadPooling.DelegateThreadPool;

namespace NTDLS.DelegateThreadPooling
{
    internal interface IQueueItemState
    {
        /// <summary>
        /// The UTC date/time which the thread worker started.
        /// </summary>
        DateTime StartTimestamp { get; set; }

        object? Parameter { get; }

        bool IsComplete { get; }
        ThreadCompleteActionDelegate<object>? ThreadCompleteAction { get; }
        ThreadActionDelegate? ThreadAction { get; }

        ParameterizedThreadActionDelegate<object>? ParameterizedThreadAction { get; }

        DelegateThreadPool OwnerThreadPool { get; }

        void SetComplete(TimeSpan? processorTime);
        void SetException(Exception ex);
    }
}
