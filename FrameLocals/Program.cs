using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace FrameLocals
{
    class Program
    {
        static void Main(string[] args)
        {
            var tasks = new List<Task>();
            for (int i = 0; i < 10; i++)
            {
                var prog = new Program();
                var task = prog.Run(args);
                tasks.Add(task);
            }

            Task.WhenAll(tasks).GetAwaiter().GetResult();
            Console.WriteLine("Done");
        }
         async Task Run(string[] args)
        {
            var a = 1;
            var b = 2;
            var c = a + b;
            Console.WriteLine(c);
        
            //Meth1();
            await Meth1Async();
        }

        void Meth1()
        {
            Console.WriteLine("Eric" + "Test");
        
            Meth2(5);
        }

        void Meth2(int p1)
        {
            var s1 = new MyStruct { Prop1 = 0, Prop2 = "S1" };
            var s2 = new MyStruct { Prop1 = 0, Prop2 = "S2" };
            var s3 = new MyStruct { Prop1 = 0, Prop2 = "S3" };

            Meth3(s3);
        }

        void Meth3(MyStruct p1)
        {
            Console.WriteLine("Press any key.");
            Console.ReadLine();
        }

        class MyStruct
        {
            public int Prop1;
            public string Prop2;
        }
    
        static async Task Meth1Async()
        {
            Console.WriteLine("Eric" + "Test");
        
            await Meth2Async(5);
        }

        static async Task Meth2Async(int p1)
        {
            var s1 = new MyStruct { Prop1 = 0, Prop2 = "S1" };
            var s2 = new MyStruct { Prop1 = 0, Prop2 = "S2" };
            var s3 = new MyStruct { Prop1 = 0, Prop2 = "S3" };

            await Task.Delay(TimeSpan.FromSeconds(1));
            Console.WriteLine("After delay");

            await Meth3Async(s3);
        }

        static async Task Meth3Async(MyStruct p1)
        {
            await Task.Delay(TimeSpan.FromSeconds(1));
            Console.WriteLine("Press any key. from async");
            await Task.Delay(TimeSpan.FromMinutes(10));
        }
    }
    
}
