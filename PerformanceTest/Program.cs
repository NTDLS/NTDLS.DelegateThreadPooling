using NTDLS.DelegateThreadPooling;
using System.Diagnostics;

namespace PerformanceTest
{
    internal class Program
    {
        private static readonly DelegateThreadPool _threadPool = new(22);

        static void Main(string[] args)
        {
            var child = _threadPool.CreateChildQueue();

            while (true)
            {
                child.Enqueue(ThreadProc);
            }

            child.WaitForCompletion();
        }

        static void ThreadProc()
        {
            Stopwatch stopwatch = Stopwatch.StartNew();

            for(int i = 0; i < 100000; i ++)
            {

            }
            stopwatch.Stop();
        }
    }
}
