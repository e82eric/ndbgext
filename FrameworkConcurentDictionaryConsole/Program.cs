using System;
using System.Collections.Concurrent;

namespace FrameworkConcurentDictionaryConsole
{
    internal class Program
    {
        public static void Main(string[] args)
        {
            var dict1 = new ConcurrentDictionary<int, string>();
            dict1.TryAdd(1, "Hello");
            dict1.TryAdd(2, "Hello");
            dict1.TryAdd(3, "Hello");
            dict1.TryAdd(4, "Hello");
            dict1.TryAdd(5, "Hello");
            dict1.TryAdd(6, "Hello");
            var dict2 = new ConcurrentDictionary<int, int>();
            dict2.TryAdd(1, 1);
            dict2.TryAdd(2, 1);
            dict2.TryAdd(3, 1);
            dict2.TryAdd(4, 1);
            dict2.TryAdd(5, 1);
            dict2.TryAdd(6, 1);
            dict2.TryAdd(7, 1);
            Console.ReadLine();
        }
    }
}