namespace FrameLocals;

class Program
{
    static async Task Main(string[] args)
    {
        var a = 1;
        var b = 2;
        var c = a + b;
        Console.WriteLine(c);
        
        Meth1();
        await Meth1Async();
    }

    static void Meth1()
    {
        Console.WriteLine("Eric" + "Test");
        
        Meth2(5);
    }

    static void Meth2(int p1)
    {
        var s1 = new MyStruct { Prop1 = 0, Prop2 = "S1" };
        var s2 = new MyStruct { Prop1 = 0, Prop2 = "S2" };
        var s3 = new MyStruct { Prop1 = 0, Prop2 = "S3" };

        Meth3(s3);
    }

    static void Meth3(MyStruct p1)
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

        await Meth3Async(s3);
    }

    static Task Meth3Async(MyStruct p1)
    {
        Console.WriteLine("Press any key.");
        Console.ReadLine();
        return Task.FromResult(0);
    }
}