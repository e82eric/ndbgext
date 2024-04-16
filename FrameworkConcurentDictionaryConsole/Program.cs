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
            var queue1 = new ConcurrentQueue<int>();
            queue1.Enqueue(1);
            queue1.Enqueue(2);
            queue1.Enqueue(3);
            var queue2 = new ConcurrentQueue<string>();
            queue2.Enqueue("Str1");
            queue2.Enqueue("Str2");
            queue2.Enqueue("Str3");
            queue2.Enqueue("Str4");
            Console.ReadLine();
        }
    }
}