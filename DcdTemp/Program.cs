using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace YourNamespace
{
    internal static class Program
    {
        private static void Main(string[] args)
        {
            var _h = new Holder();

            var a = "!tquery -mt 00007ff9ec60ee58 select <MyProp>k__BackingField where <MyProp>k__BackingField == Test1";
            var a2 = "-mt 00007ff9ec611ca8 select <MyProp>k__BackingField,<MyGuidProp>k__BackingField where <MyProp>k__BackingField == Test1";

            var expression = @"^-(?<source>mt|addr|array)\s+(?<address>\w+)\s+select\s+(?<fields>[^,]+(?:\s*,\s*[^,]+)*)\s+where\s+(?<predicate>.*)$";
            var selectWhereRegex = new Regex(expression, RegexOptions.IgnoreCase);
            var selectWhereResult = selectWhereRegex.Match(a2);

            var dict = new ConcurrentDictionary<int, ValClass>();
            dict.TryAdd(1, new ValClass
            {
                MyProp = "Test1",
                MyGuidProp = Guid.Empty,
                Sem = new SemaphoreSlim(2),
                Dt = DateTime.Parse("3/17/2025 10:52:04 PM"),
                Dto = DateTimeOffset.Parse("3/17/2025 10:52:04 PM"),
                SC1 = new SubClass1 { SC2 = new SubClass2 { G = Guid.NewGuid() } },
                DtProp = DateTime.Parse("3/17/2025 10:52:04 PM"),
            });

            dict.TryAdd(2, new ValClass
            {
                MyProp = "Test2",
                MyGuidProp = Guid.NewGuid(),
                Sem = new SemaphoreSlim(3),
                Dt = DateTime.Now,
                Dto = DateTimeOffset.Now,
                SC1 = new SubClass1 { SC2 = new SubClass2 { G = Guid.NewGuid() } },
                DtProp = DateTime.Parse("2025-01-05")
            });

            var ht = new Hashtable();
            ht.Add(1, "test1");
            ht.Add(2, "test2");
            ht.Add(3, "test3");

            var ericArray = new[]
            {
                new ValClass { MyProp = "Test 1" },
                new ValClass { MyProp = "Test 2" },
                new ValClass { MyProp = "Test 3" },
                new ValClass { MyProp = "Test 4" },
            };
            
            TaskStackRun().GetAwaiter().GetResult();

            var runner = new Runner();
            var sem = new SemaphoreSlim(1); // unused in original sample, keeping for parity

            var tasks = new List<Task>();
            for (var i = 0; i < 5; i++)
            {
                tasks.Add(runner.Run());
            }

            Task.WhenAll(tasks).GetAwaiter().GetResult();
            Console.ReadLine();
        }
        
        public static async Task TaskStackRun()
        {
            await Frame1();
        }

        public static async Task Frame1()
        {
            await Frame2();
        }
    
        static async Task Frame2()
        {
            Console.WriteLine("Waiting for a day");
            await Task.Delay(TimeSpan.FromDays(1));
        }
    }
    
    class SubClass2
    {
        // .NET Framework: no init-only setters unless you have a newer compiler + IsExternalInit workaround
        public Guid G { get; set; }
    }

    class SubClass1
    {
        public SubClass2 SC2 { get; set; }
    }

    class ValClass
    {
        public string MyProp { get; set; }
        public Guid MyGuidProp { get; set; }
        public SemaphoreSlim Sem { get; set; }
        public DateTime Dt { get; set; }
        public DateTimeOffset Dto { get; set; }
        public SubClass1 SC1 { get; set; }
        public DateTime? DtProp { get; set; }
    }

    public class Runner
    {
        // .NET Framework: replace "new(1)" with explicit type
        private static readonly SemaphoreSlim _sem = new SemaphoreSlim(1);

        public async Task Run()
        {
            await _sem.WaitAsync().ConfigureAwait(false);
            try
            {
                // simulate work
                await Task.Delay(50).ConfigureAwait(false);
            }
            finally
            {
                _sem.Release();
            }
        }
    }

    enum MyEnum { Zero, One, Two }

    class Holder
    {
        public object BoxedInt = 42;          // boxed
        public int? NullableI = 123;          // Nullable<int>
        public int PlainI = 99;               // inline
        public double D = 1.23;               // double
        public long L = 123456789L;           // Int64
        public MyEnum E = MyEnum.Two;         // enum
    }
}