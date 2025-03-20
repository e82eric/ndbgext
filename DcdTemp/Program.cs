// See https://aka.ms/new-console-template for more information

using System.Collections.Concurrent;

var dict = new ConcurrentDictionary<int, ValClass>();
dict.TryAdd(1, new ValClass
{
    MyProp = "Test1",
    MyGuidProp = Guid.Empty,
    Sem = new SemaphoreSlim(2),
    Dt = DateTime.Parse("3/17/2025 10:52:04 PM"),
    Dto = DateTimeOffset.Parse("3/17/2025 10:52:04 PM"),
    SC1 = new SubClass1{SC2 = new SubClass2{G = Guid.NewGuid()}}
});
dict.TryAdd(2, new ValClass
{
    MyProp = "Test2",
    MyGuidProp = Guid.NewGuid(),
    Sem = new SemaphoreSlim(3),
    Dt = DateTime.Now, Dto = DateTimeOffset.Now,
    SC1 = new SubClass1{SC2 = new SubClass2{G = Guid.NewGuid()}}
});

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

class SubClass2
{
    public Guid G { get; init; }
}

class SubClass1
{
    public SubClass2 SC2 { get; init; }
}

class ValClass
{
    public string MyProp { get; set; }
    public Guid MyGuidProp { get; set; }
    public SemaphoreSlim Sem { get; set; }
    public DateTime Dt { get; set; }
    public DateTimeOffset Dto { get; set; }
    public SubClass1 SC1 { get; init; }
}

public class Runner
{
    private static SemaphoreSlim _sem = new(1);
    public async Task Run()
    {
        await _sem.WaitAsync();
    }
}
