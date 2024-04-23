using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ndbgext;

public static unsafe class Extension
{
    [UnmanagedCallersOnly(EntryPoint = "DebugExtensionInitialize")]
    public static unsafe int DebugExtensionInitialize(uint* version, uint* flags)
    {
        *version = (1 & 0xffff) << 16;
        *flags = 0;
        return 0;
    }

    [UnmanagedCallersOnly(EntryPoint = "help", CallConvs = new[] { typeof(CallConvStdcall) })]
    public static int Help(nint pUnknown, nint args)
    {
        Console.WriteLine("clruniqstack");
        Console.WriteLine("threadpoolqueue (tpq) -detail");
        Console.WriteLine("threadpoolstats (tps)");
        Console.WriteLine("dumpconcurrentdict (dcd)");
        Console.WriteLine("dumpconcurrentqueue (dcq)");
        Console.WriteLine("getmetodname (dcq) [methodptr]");
        Console.WriteLine("tasks (tks) -detail");
        Console.WriteLine("dumpgen [gen0|gen1|gen2]");
        Console.WriteLine("blockinginfo");
        return 0;
    }

    [UnmanagedCallersOnly(EntryPoint = "clruniqstack", CallConvs = new[] { typeof(CallConvStdcall) })]
    public static int UniqClrStack(nint pUnknown, nint args)
    {
        try
        {
            ClrUniqStack cmd = new(pUnknown);
            string? arguments = Marshal.PtrToStringAnsi(args);
            cmd.Run(arguments ?? "");
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"Failed to run {nameof(UniqClrStack)} command.");
            Console.Error.WriteLine(e);
        }

        return 0;
    }

    [UnmanagedCallersOnly(EntryPoint = "threads", CallConvs = new[] { typeof(CallConvStdcall) })]
    public static int Threads(nint pUnknown, nint args)
    {
        try
        {
            Threads cmd = new(pUnknown);
            string? arguments = Marshal.PtrToStringAnsi(args);
            cmd.Run(arguments ?? "");
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"Failed to run {nameof(Threads)} command.");
            Console.Error.WriteLine(e);
        }

        return 0;
    }

    [UnmanagedCallersOnly(EntryPoint = "dcd", CallConvs = new[] { typeof(CallConvStdcall) })]
    public static int DumpCDict(nint pUnknown, nint args)
    {
        return _DumpConcurDict(pUnknown, args);
    }

    [UnmanagedCallersOnly(EntryPoint = "dumpconcurrentdict", CallConvs = new[] { typeof(CallConvStdcall) })]
    public static int DumpConcurDict(nint pUnknown, nint args)
    {
        return _DumpConcurDict(pUnknown, args);
    }

    private static int _DumpConcurDict(nint pUnknown, nint args)
    {
        try
        {
            ConcurrentDictionary cmd = new(pUnknown);
            string? arguments = Marshal.PtrToStringAnsi(args);
            cmd.Run(arguments ?? "");
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"Failed to run {nameof(ConcurrentDictionary)} command.");
            Console.Error.WriteLine(e);
        }

        return 0;
    }

    [UnmanagedCallersOnly(EntryPoint = "dcq", CallConvs = new[] { typeof(CallConvStdcall) })]
    public static int dcd(nint pUnknown, nint args)
    {
        return _DumpConcurQueue(pUnknown, args);
    }

    [UnmanagedCallersOnly(EntryPoint = "dumpconcurrentqueue", CallConvs = new[] { typeof(CallConvStdcall) })]
    public static int DumpConcurQueue(nint pUnknown, nint args)
    {
        return _DumpConcurQueue(pUnknown, args);
    }

    private static int _DumpConcurQueue(nint pUnknown, nint args)
    {
        try
        {
            ConcurrentQueueCommand cmd = new(pUnknown);
            string? arguments = Marshal.PtrToStringAnsi(args);
            cmd.Run(arguments ?? "");
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"Failed to run {nameof(ConcurrentQueueCommand)} command.");
            Console.Error.WriteLine(e);
        }

        return 0;
    }

    [UnmanagedCallersOnly(EntryPoint = "tpq", CallConvs = new[] { typeof(CallConvStdcall) })]
    public static int Tps(nint pUnknown, nint args)
    {
        return _ThreadPoolQueue(pUnknown, args);
    }

    [UnmanagedCallersOnly(EntryPoint = "threadpoolqueue", CallConvs = new[] { typeof(CallConvStdcall) })]
    public static int ThreadPoolQueue(nint pUnknown, nint args)
    {
        return _ThreadPoolQueue(pUnknown, args);
    }

    private static readonly ThreadPool _threadPool = new ThreadPool(new ConcurrentQueue());
    private static int _ThreadPoolQueue(nint pUnknown, nint args)
    {
        try
        {
            ThreadPoolCommand cmd = new(pUnknown, _threadPool);
            var arguments = Marshal.PtrToStringAnsi(args);
            cmd.Run(arguments ?? "");
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"Failed to run {nameof(ThreadPoolCommand)} command.");
            Console.Error.WriteLine(e);
        }

        return 0;
    }

    [UnmanagedCallersOnly(EntryPoint = "threadpoolstats", CallConvs = new[] { typeof(CallConvStdcall) })]
    public static int ThreadPoolStats(nint pUnknown, nint args)
    {
        return _ThreadPoolStats(pUnknown, args);
    }

    [UnmanagedCallersOnly(EntryPoint = "tps", CallConvs = new[] { typeof(CallConvStdcall) })]
    public static int TPS(nint pUnknown, nint args)
    {
        return _ThreadPoolStats(pUnknown, args);
    }
    private static int _ThreadPoolStats(nint pUnknown, nint args)
    {
        try
        {
            ThreadPoolCommand cmd = new(pUnknown, _threadPool);
            var arguments = Marshal.PtrToStringAnsi(args);
            cmd.RunRunning(arguments ?? "");
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"Failed to run {nameof(ThreadPoolCommand)} command.");
            Console.Error.WriteLine(e);
        }

        return 0;
    }

    [UnmanagedCallersOnly(EntryPoint = "getmethodname", CallConvs = new[] { typeof(CallConvStdcall) })]
    public static int GetMethodName(nint pUnknown, nint args)
    {
        return _GetMethodName(pUnknown, args);
    }

    [UnmanagedCallersOnly(EntryPoint = "gmn", CallConvs = new[] { typeof(CallConvStdcall) })]
    public static int GMN(nint pUnknown, nint args)
    {
        return _GetMethodName(pUnknown, args);
    }

    private static int _GetMethodName(nint pUnknown, nint args)
    {
        try
        {
            GetMethodNameCommand cmd = new(pUnknown);
            string? arguments = Marshal.PtrToStringAnsi(args);
            cmd.Run(arguments ?? "");
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"Failed to run {nameof(GetMethodNameCommand)} command.");
            Console.Error.WriteLine(e);
        }

        return 0;
    }

    [UnmanagedCallersOnly(EntryPoint = "tasks", CallConvs = new[] { typeof(CallConvStdcall) })]
    public static int Tasks(nint pUnknown, nint args)
    {
        return _Tasks(pUnknown, args);
    }

    [UnmanagedCallersOnly(EntryPoint = "tks", CallConvs = new[] { typeof(CallConvStdcall) })]
    public static int TKS(nint pUnknown, nint args)
    {
        return _Tasks(pUnknown, args);
    }

    private static Tasks _tasks = new Tasks();
    private static int _Tasks(nint pUnknown, nint args)
    {
        try
        {
            TasksCommand cmd = new(_tasks, pUnknown);
            string? arguments = Marshal.PtrToStringAnsi(args);
            cmd.Run(arguments ?? "");
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"Failed to run {nameof(TasksCommand)} command.");
            Console.Error.WriteLine(e);
        }

        return 0;
    }

    [UnmanagedCallersOnly(EntryPoint = "dumpgen", CallConvs = new[] { typeof(CallConvStdcall) })]
    public static int DumpGen(nint pUnknown, nint args)
    {
        return _DumGen(pUnknown, args);
    }

    private static int _DumGen(nint pUnknown, nint args)
    {
        try
        {
            GcGenerationInfoCommand cmd = new(pUnknown);
            string? arguments = Marshal.PtrToStringAnsi(args);
            cmd.Run(arguments ?? "");
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"Failed to run {nameof(GcGenerationInfoCommand)} command.");
            Console.Error.WriteLine(e);
        }

        return 0;
    }

    private static BlockingInfoProvider _thread = new BlockingInfoProvider();
    [UnmanagedCallersOnly(EntryPoint = "blockinginfo", CallConvs = new[] { typeof(CallConvStdcall) })]
    public static int BlockingInfo(nint pUnknown, nint args)
    {
        return _BlockingInfo(pUnknown, args);
    }
    
    private static int _BlockingInfo(nint pUnknown, nint args)
    {
        try
        {
            BlockingInfoCommand cmd = new(_thread, pUnknown);
            string? arguments = Marshal.PtrToStringAnsi(args);
            cmd.Run(arguments ?? "");
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"Failed to run {nameof(BlockingInfoCommand)} command.");
            Console.Error.WriteLine(e);
        }

        return 0;
    }
}
