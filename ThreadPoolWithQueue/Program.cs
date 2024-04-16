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

            for (int i = 0; i < 500; i++)
            {
                ThreadPool.QueueUserWorkItem(TestCallback, i);
            }

            for (var i = 0; i < 2250; i++)
            {
                var i1 = i;

                void ForTask()
                {
                    Console.WriteLine("Started {0}", i1);
                    Thread.Sleep(TimeSpan.FromMinutes(3));
                }

                Task.Run(ForTask);
            }

            Console.ReadLine();
        }
    }
}

