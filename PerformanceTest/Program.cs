using NTDLS.DelegateThreadPooling;

namespace PerformanceTest
{
    internal class Program
    {
        static void Main()
        {
            var configuration = new DelegateThreadPoolConfiguration();
            var dtp = new DelegateThreadPool(configuration);

            var childPool = dtp.CreateChildPool();

            for (int item = 0; item < 100000; item++)
            {
                childPool.Enqueue(() =>
                {
                    Guid param = Guid.NewGuid();
                    for (int workload = 0; workload < 10000; workload++)
                    {
                        foreach (var c in param.ToString())
                        {
                            // Simulate light string processing
                        }
                    }
                });
            }

            childPool.WaitForCompletion();

            Console.WriteLine($"Start Thread Count: {configuration.InitialThreadCount}, Final: {dtp.ThreadCount:n0}");
            Console.WriteLine($"Parallel Duration: {childPool.TotalDurationMs:n0}, CPU Time: {childPool.TotalProcessorTimeMs:n0}.");

            Console.WriteLine("Thread stats:");
            foreach (var thread in dtp.Threads)
            {
                Console.WriteLine($"Thread: {thread.ManagedThread.ManagedThreadId}, CPU Time: {thread.NativeThread?.TotalProcessorTime.TotalMilliseconds:n0}.");
            }

            dtp.Stop();

        }
    }
}
