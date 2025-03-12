// See https://aka.ms/new-console-template for more information

using System.Collections.Concurrent;

var dict = new ConcurrentDictionary<int, ValClass>();
dict.TryAdd(1, new ValClass { MyProp = "Test1", MyGuidProp = Guid.Empty});
dict.TryAdd(2, new ValClass { MyProp = "Test2", MyGuidProp = Guid.NewGuid()});

var sem = new SemaphoreSlim(1);

for (var i = 0; i < 5; i++)
{
    Task.Run(() => { sem.Wait(); });
}
Console.ReadLine();

class ValClass
{
    public string MyProp { get; set; }
    public Guid MyGuidProp { get; set; }
}
