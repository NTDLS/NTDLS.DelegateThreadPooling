using NTDLS.DelegateThreadPooling;
using System.Diagnostics;
using System.Threading;

namespace PerformanceTest
{
    internal class Program
    {
        private static readonly DelegateThreadPool _threadPool = new(22);

        static void Main(string[] args)
        {
            var dtp = new DelegateThreadPool();

            var childPool = dtp.CreateChildPool<Guid>();

            for (int item = 0; item < 100000; item++)
            {
                childPool.Enqueue(Guid.NewGuid(), (param) =>
                {
                    for (int workload = 0; workload < 10000; workload++)
                    {
                        foreach (var c in param.ToString())
                        {
                        }
                    }
                });
            }

            childPool.WaitForCompletion();
            Console.WriteLine($"Parallel Duration: {childPool.TotalDuration:n0}, CPU Time: {childPool.TotalProcessorTime:n0}.");

            Console.WriteLine("Thread stats:");
            foreach (var thread in dtp.Threads)
            {
                Console.WriteLine($"Thread: {thread.ManagedThread.ManagedThreadId}, CPU Time: {thread.NativeThread?.TotalProcessorTime.TotalMilliseconds:n0}.");
            }

            dtp.Stop();
        }
    }
}
