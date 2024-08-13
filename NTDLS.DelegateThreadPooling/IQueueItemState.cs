using static NTDLS.DelegateThreadPooling.DelegateThreadPool;

namespace NTDLS.DelegateThreadPooling
{
    internal interface IQueueItemState
    {
        object? Parameter { get; }

        bool IsComplete { get; }
        ThreadCompleteActionDelegate<object>? ThreadCompleteAction { get; }
        ThreadActionDelegate? ThreadAction { get; }

        ParameterizedThreadActionDelegate<object>? ParameterizedThreadAction { get; }

        DelegateThreadPool OwnerThreadPool { get; }

        void SetComplete();
        void SetException(Exception ex);
    }
}
