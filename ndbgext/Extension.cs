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
        Console.WriteLine("dumpconcurrentdict (dcd) -list [containsFilter]");
        Console.WriteLine("dumpconcurrentqueue (dcq) -list [containsFilter]");
        Console.WriteLine("getmetodname (gmn) [methodptr]");
        Console.WriteLine("tasks (tks) -detail [state]");
        Console.WriteLine("dumpgen [gen0|gen1|gen2]");
        Console.WriteLine("blockinginfo");
        Console.WriteLine("heapstat");
        Console.WriteLine("decompilemethod -sp [address] | -ip [instructionPointer] | -md [methodDesc]");
        Console.WriteLine("decompiletype -ad [address] | -nm [fullTypeName]");
        Console.WriteLine("savemodule [modulename]");
        Console.WriteLine("findref -recurse[r] address");
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
            ConcurrentDictionaryCommand cmd = new(pUnknown);
            string? arguments = Marshal.PtrToStringAnsi(args);
            cmd.Run(arguments ?? "");
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"Failed to run {nameof(ConcurrentDictionaryCommand)} command.");
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

    private static readonly GcGenerationInfoProvider GcGenerationInfoProvider = new GcGenerationInfoProvider();
    private static int _DumGen(nint pUnknown, nint args)
    {
        try
        {
            GcGenerationInfoCommand cmd = new(GcGenerationInfoProvider, pUnknown);
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

    private static readonly BlockingInfoProvider BlockingInfoProvider = new BlockingInfoProvider();
    [UnmanagedCallersOnly(EntryPoint = "blockinginfo", CallConvs = new[] { typeof(CallConvStdcall) })]
    public static int BlockingInfo(nint pUnknown, nint args)
    {
        return _BlockingInfo(pUnknown, args);
    }
    
    private static int _BlockingInfo(nint pUnknown, nint args)
    {
        try
        {
            BlockingInfoCommand cmd = new(BlockingInfoProvider, pUnknown);
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
    
    [UnmanagedCallersOnly(EntryPoint = "heapstat", CallConvs = new[] { typeof(CallConvStdcall) })]
    public static int HeapStat(nint pUnknown, nint args)
    {
        return _HeapStat(pUnknown, args);
    }

    private static readonly HeapStatProvider HeapStatProvider = new HeapStatProvider();
    private static int _HeapStat(nint pUnknown, nint args)
    {
        try
        {
            HeapStatCommand cmd = new(HeapStatProvider, pUnknown);
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
    
    [UnmanagedCallersOnly(EntryPoint = "findref", CallConvs = new[] { typeof(CallConvStdcall) })]
    public static int FindRef(nint pUnknown, nint args)
    {
        return _FindRef(pUnknown, args);
    }
    
    private static readonly FindRefProvider FindRefProvider = new();
    private static int _FindRef(nint pUnknown, nint args)
    {
        try
        {
            FindRefCommand cmd = new(FindRefProvider, pUnknown);
            string? arguments = Marshal.PtrToStringAnsi(args);
            cmd.Run(arguments ?? "");
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"Failed to run {nameof(FindRefCommand)} command.");
            Console.Error.WriteLine(e);
        }

        return 0;
    }
    
    [UnmanagedCallersOnly(EntryPoint = "savemodule", CallConvs = new[] { typeof(CallConvStdcall) })]
    public static int SaveModule(nint pUnknown, nint args)
    {
        return _SaveModule(pUnknown, args);
    }
    
    private static readonly SaveModuleProvider SaveModuleProvider = new(new DllExtractor());
    private static int _SaveModule(nint pUnknown, nint args)
    {
        try
        {
            SaveModuleCommand cmd = new(SaveModuleProvider, pUnknown);
            string? arguments = Marshal.PtrToStringAnsi(args);
            cmd.Run(arguments ?? "");
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"Failed to run {nameof(SaveModuleCommand)} command.");
            Console.Error.WriteLine(e);
        }

        return 0;
    }
    
    [UnmanagedCallersOnly(EntryPoint = "decompilemethod", CallConvs = new[] { typeof(CallConvStdcall) })]
    public static int Decompile(nint pUnknown, nint args)
    {
        return _DecompileCurrentFrame(pUnknown, args);
    }
    
    private static readonly DecompileMethodProvider DecompileMethodProvider = new(new Decompiler(new DllExtractor()));
    private static int _DecompileCurrentFrame(nint pUnknown, nint args)
    {
        try
        {
            DecompileMethodCommand cmd = new(DecompileMethodProvider, pUnknown);
            string? arguments = Marshal.PtrToStringAnsi(args);
            cmd.Run(arguments ?? "");
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"Failed to run {nameof(DecompileMethodCommand)} command.");
            Console.Error.WriteLine(e);
        }

        return 0;
    }
    
    [UnmanagedCallersOnly(EntryPoint = "decompiletype", CallConvs = new[] { typeof(CallConvStdcall) })]
    public static int DecompileType(nint pUnknown, nint args)
    {
        return _DecompileType(pUnknown, args);
    }
    
    private static readonly DecompileTypeProvider DecompileTypeProvider = new(new Decompiler(new DllExtractor()));
    private static int _DecompileType(nint pUnknown, nint args)
    {
        try
        {
            DecompileTypeCommand cmd = new(DecompileTypeProvider, pUnknown);
            string? arguments = Marshal.PtrToStringAnsi(args);
            cmd.Run(arguments ?? "");
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"Failed to run {nameof(DecompileTypeCommand)} command.");
            Console.Error.WriteLine(e);
        }

        return 0;
    }
    
    [UnmanagedCallersOnly(EntryPoint = "taskcallstack", CallConvs = new[] { typeof(CallConvStdcall) })]
    public static int TaskCallStack(nint pUnknown, nint args)
    {
        return _TaskCallStack(pUnknown, args);
    }
    
   private static readonly DumpAsyncCommand DumpAsyncCommand = new();
    private static int _TaskCallStack(nint pUnknown, nint args)
    {
        try
        {
            TaskCallStackCommand cmd = new(DumpAsyncCommand, pUnknown);
            string? arguments = Marshal.PtrToStringAnsi(args);
            cmd.Run(arguments ?? "");
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"Failed to run {nameof(TaskCallStackCommand)} command.");
            Console.Error.WriteLine(e);
        }

        return 0;
    }
}
