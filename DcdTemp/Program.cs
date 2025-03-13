// See https://aka.ms/new-console-template for more information

using System.Collections.Concurrent;

var dict = new ConcurrentDictionary<int, ValClass>();
dict.TryAdd(1, new ValClass { MyProp = "Test1", MyGuidProp = Guid.Empty, Sem = new SemaphoreSlim(2)});
dict.TryAdd(2, new ValClass { MyProp = "Test2", MyGuidProp = Guid.NewGuid(), Sem = new SemaphoreSlim(3)});

var runner = new Runner();
var sem = new SemaphoreSlim(1);

var tasks = new List<Task>();
for (var i = 0; i < 5; i++)
{
    var task = runner.Run();
    tasks.Add(task);
}

Task.WhenAll(tasks).GetAwaiter().GetResult();
Console.ReadLine();

class ValClass
{
    public string MyProp { get; set; }
    public Guid MyGuidProp { get; set; }
    public SemaphoreSlim Sem { get; set; }
}

public class Runner
{
    private static SemaphoreSlim _sem = new(1);
    public async Task Run()
    {
        await _sem.WaitAsync();
    }
}
