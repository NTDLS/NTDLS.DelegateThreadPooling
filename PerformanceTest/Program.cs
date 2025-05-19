using NTDLS.DelegateThreadPooling;
using System.Diagnostics;

namespace PerformanceTest
{
    internal class Program
    {
        static DelegateThreadPool dtp = new();

        static void Main(string[] args)
        {
            Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.High;

            Console.WriteLine("Items DTP Task");
            for (int iteration = 0; iteration < 500; iteration++)
            {
                int items = (iteration == 0 ? 1 : iteration) * 100;

                //int items = 100;

                var startTime = DateTime.UtcNow;
                TestThreadPool(items);
                double dtpTime = (DateTime.UtcNow - startTime).TotalMilliseconds;

                startTime = DateTime.UtcNow;
                TestTasks(items);
                double taskTime = (DateTime.UtcNow - startTime).TotalMilliseconds;

                Console.WriteLine($"{items:n0} {dtpTime:n2} {taskTime:n2}");
            }

            dtp.Stop();

        }

        static void TestThreadPool(int items)
        {
            var childPool = dtp.CreateChildPool<Guid>();

            for (int item = 0; item < items; item++)
            {
                childPool.Enqueue(Guid.NewGuid(), (param) =>
                {
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

            /*
            Console.WriteLine($"Start Thread Count: {configuration.InitialThreadCount}, Final: {dtp.ThreadCount:n0}");
            Console.WriteLine($"Parallel Duration: {childPool.TotalDurationMs:n0}, CPU Time: {childPool.TotalProcessorTimeMs:n0}.");

            Console.WriteLine("Thread stats:");
            foreach (var thread in dtp.Threads)
            {
                Console.WriteLine($"Thread: {thread.ManagedThread.ManagedThreadId}, CPU Time: {thread.NativeThread?.TotalProcessorTime.TotalMilliseconds:n0}.");
            }
            */
        }

        private static void TestTasks(int items)
        {
            var tasks = new List<Task>();

            for (int item = 0; item < items; item++)
            {
                Guid param = Guid.NewGuid(); // capture value

                var task = Task.Run(() =>
                {
                    for (int workload = 0; workload < 10000; workload++)
                    {
                        foreach (var c in param.ToString())
                        {
                            // Simulate light string processing
                        }
                    }
                });

                tasks.Add(task);
            }

            Task.WaitAll(tasks);
        }
    }
}
