using static NTDLS.DelegateThreadPooling.DelegateThreadPool;

namespace NTDLS.DelegateThreadPooling
{
    internal interface IQueueItemState
    {
        object? Parameter { get; }

        bool IsComplete { get; }
        ThreadCompleteAction? OnComplete { get; }
        ThreadAction? ThreadAction { get; }
        //ParameterizedThreadAction<T>? ParameterizedThreadAction { get; }

        ParameterizedThreadAction<object>? ParameterizedThreadAction { get; }

        DelegateThreadPool OwnerThreadPool { get; }

        void SetComplete();
        void SetException(Exception ex);
    }
}
