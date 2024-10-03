using NTDLS.DelegateThreadPooling;
using System.Diagnostics;

namespace PerformanceTest
{
    internal class Program
    {
        private static readonly DelegateThreadPool _threadPool = new(22);

        static void Main(string[] args)
        {
            var dtp = new DelegateThreadPool();

            var childQueue = dtp.CreateChildQueue<Guid>();

            for (int item = 0; item < 10000; item++)
            {
                childQueue.Enqueue(Guid.NewGuid(), (param) =>
                {
                    for (int workload = 0; workload < 10000; workload++)
                    {
                        foreach (var c in param.ToString())
                        {
                        }
                    }
                });
            }

            childQueue.WaitForCompletion();

            foreach (var thread in dtp.Threads)
            {
                Console.WriteLine($"Thread: {thread.ManagedThread.ManagedThreadId}: {thread.NativeThread?.TotalProcessorTime.TotalMilliseconds:n0} ");
            }
        }
    }
}
