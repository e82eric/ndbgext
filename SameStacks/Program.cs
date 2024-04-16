using System.Collections.Concurrent;

ThreadPool.SetMaxThreads(1, 1);

var a = new ConcurrentDictionary<string, string>();
a.TryAdd("Hello", "World");
a.TryAdd("Hello2", "World");
a.TryAdd("Hello3", "World");
a.TryAdd("Hello4", "World");
a.TryAdd("Hello5", "World");
a.TryAdd("Hello6", "World");
a.TryAdd("Hello7", "World");
a.TryAdd("Hello8", "World");
a.TryAdd("Hello9", "World");

var queue1 = new ConcurrentQueue<int>();
queue1.Enqueue(1);
queue1.Enqueue(2);
queue1.Enqueue(3);
var queue2 = new ConcurrentQueue<string>();
queue2.Enqueue("Str1");
queue2.Enqueue("Str2");
queue2.Enqueue("Str3");
queue2.Enqueue("Str4");

void Meth1()
{
    Meth2();
}
void Meth2()
{
    Meth3();
}
void Meth3()
{
    Thread.Sleep(TimeSpan.FromMinutes(1));
}

for (var i = 0; i < 10; i++)
{
    Task.Run(Meth1);
}

Console.ReadLine();
