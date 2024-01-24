namespace NTDLS.DelegateThreadPooling
{
    internal class PooledThreadEnvelope
    {
        public enum PooledThreadState
        {
            Idle,
            Executing
        }

        public Thread Thread { get; private set; }
        public AutoResetEvent WaitEvent { get; private set; } = new(true);
        public PooledThreadState State { get; set; }

        public void Signal() => WaitEvent.Set();
        public void Join() => Thread.Join();
        public bool Wait() => WaitEvent.WaitOne();

        public PooledThreadEnvelope(ParameterizedThreadStart proc)
        {
            State = PooledThreadState.Idle;
            Thread = new Thread(proc);
            Thread.Start(this);
        }
    }
}
