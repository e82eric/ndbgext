using System.Collections.Concurrent;


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
