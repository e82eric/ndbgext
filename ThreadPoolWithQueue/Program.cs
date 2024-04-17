using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace ThreadPoolWithQueue
{
    internal class Program
    {
        public static void Main(string[] args)
        {
            Console.WriteLine("Hello, World!");
            ThreadPool.SetMaxThreads(33, 33);

            void TestCallback(object ctr)
            {
                Console.WriteLine("Started {0}", ctr);
                Thread.Sleep(TimeSpan.FromMinutes(3));
            }

            for (int i = 0; i < 5; i++)
            {
                ThreadPool.QueueUserWorkItem(TestCallback, i);
            }

            for (var i = 0; i < 2000; i++)
            {
                var i1 = i;

                void ForTask()
                {
                    Console.WriteLine("Started {0}", i1);
                    Thread.Sleep(TimeSpan.FromMinutes(3));
                }

                Task.Run(ForTask);
            }


            System.Threading.ThreadPool.GetAvailableThreads(out var availWorker, out var availIo);
            Console.WriteLine("Avail worker{0} Avail io {1}");
            Console.ReadLine();
        }
    }
}

