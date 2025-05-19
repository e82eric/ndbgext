// See https://aka.ms/new-console-template for more information

using System.Collections;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;

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
    SC1 = new SubClass1{SC2 = new SubClass2{G = Guid.NewGuid()}},
    DtProp = DateTime.Parse("3/17/2025 10:52:04 PM"),
});
dict.TryAdd(2, new ValClass
{
    MyProp = "Test2",
    MyGuidProp = Guid.NewGuid(),
    Sem = new SemaphoreSlim(3),
    Dt = DateTime.Now, Dto = DateTimeOffset.Now,
    SC1 = new SubClass1{SC2 = new SubClass2{G = Guid.NewGuid()}},
    DtProp = DateTime.Parse("2025-01-05")
});

var ht = new Hashtable();
ht.Add(1, "test1");
ht.Add(2, "test2");
ht.Add(3, "test3");

var ericArray = new[]
{
    new ValClass() { MyProp = "Test 1" },
    new ValClass() { MyProp = "Test 2" },
    new ValClass() { MyProp = "Test 3" },
    new ValClass() { MyProp = "Test 4" },
};

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
    public DateTime? DtProp { get; set; }
}

public class Runner
{
    private static SemaphoreSlim _sem = new(1);
    public async Task Run()
    {
        await _sem.WaitAsync();
    }
}
enum MyEnum { Zero, One, Two }
class Holder
{
    public object   BoxedInt  =  42;          // boxed
    public int?     NullableI = 123;          // struct
    public int      PlainI    =  99;          // inline
    public double   D         = 1.23;         // double
    public long     L         = 123456789L;   // Int64
    public MyEnum   E         = MyEnum.Two;   // enum
}
