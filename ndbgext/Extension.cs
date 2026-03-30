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
        string? arguments = Marshal.PtrToStringAnsi(args)?.Trim();

        if (string.Equals(arguments, "tquery", StringComparison.OrdinalIgnoreCase))
        {
            PrintTqueryHelp();
            return 0;
        }

        if (string.Equals(arguments, "decompilemethod", StringComparison.OrdinalIgnoreCase))
        {
            PrintDecompileMethodHelp();
            return 0;
        }

        if (string.Equals(arguments, "decompiletype", StringComparison.OrdinalIgnoreCase))
        {
            PrintDecompileTypeHelp();
            return 0;
        }

        if (string.Equals(arguments, "buildobjectgraph", StringComparison.OrdinalIgnoreCase))
        {
            PrintBuildObjectGraphHelp();
            return 0;
        }

        if (string.Equals(arguments, "retainedbytestat", StringComparison.OrdinalIgnoreCase))
        {
            PrintRetainedByteStatHelp();
            return 0;
        }

        if (string.Equals(arguments, "referredfrom", StringComparison.OrdinalIgnoreCase))
        {
            PrintReferredFromHelp();
            return 0;
        }

        if (string.Equals(arguments, "refferedtotree", StringComparison.OrdinalIgnoreCase))
        {
            PrintRefferedToTreeHelp();
            return 0;
        }

        Console.WriteLine("clruniqstack");
        Console.WriteLine("taskcallstack");
        Console.WriteLine("threadpoolqueue (tpq) -detail");
        Console.WriteLine("threadpoolstats (tps)");
        Console.WriteLine("dumpconcurrentdict (dcd) | -list [containsFilter] | -count");
        Console.WriteLine("dumpconcurrentqueue (dcq) | -list [containsFilter]");
        Console.WriteLine("getmetodname (gmn) [methodptr]");
        Console.WriteLine("tasks (tks) -detail [state]");
        Console.WriteLine("blockinginfo");
        Console.WriteLine("decompilemethod -sp [address] | -ip [instructionPointer] | -md [methodDesc]");
        Console.WriteLine("decompiletype [address] | -ad [address] | -nm [typeName] | -ip [ip] | -md [token] | -mt [methodTable]");
        Console.WriteLine("savemodule [modulename]");
        Console.WriteLine("inclusivebytes -n [N]");
        Console.WriteLine("buildmemorygraph");
        Console.WriteLine("buildobjectgraph");
        Console.WriteLine("retainedbytestat [--min-retained-bytes N]");
        Console.WriteLine("referredfrom <TypeName> [--top N]");
        Console.WriteLine("refferedtotree <TypeName> [--levels N]");
        Console.WriteLine("totaltypebytes");
        Console.WriteLine("typebytes <TypeName> [--max-type-roots N] [--max-tree-nodes N] [--max-depth N] [--quiet]");
        Console.WriteLine("tquery [-short] [-debug] (-mt|-addr|-array|-implements) <address> (select|where)");
        Console.WriteLine();
        Console.WriteLine("Use !til.help <command> for detailed help on a specific command.");

        return 0;
    }

    private static void PrintTqueryHelp()
    {
        Console.WriteLine("tquery [-short] [-debug] (-mt|-addr|-array|-implements) <address> (select <fields> [where <expr>] | where <expr>)");
        Console.WriteLine();
        Console.WriteLine("  Source modes:");
        Console.WriteLine("    -mt <methodTable>       Query all heap objects with the given method table");
        Console.WriteLine("    -addr <address>         Query a single object at the given address");
        Console.WriteLine("    -array <address>        Query elements of the array at the given address");
        Console.WriteLine("    -implements <typeName>   Query all objects whose type implements/extends the given type name");
        Console.WriteLine();
        Console.WriteLine("  Clauses:");
        Console.WriteLine("    select <f1,f2,...>       Project specific fields from each matching object");
        Console.WriteLine("    where <predicate>        Filter objects matching the predicate");
        Console.WriteLine("    select ... where ...     Combine projection and filtering");
        Console.WriteLine();
        Console.WriteLine("  Field expressions:");
        Console.WriteLine("    fieldName                Direct field on the object");
        Console.WriteLine("    field1.field2            Nested field traversal (follows references and value types)");
        Console.WriteLine("    *                        All fields on the object");
        Console.WriteLine("    field1.*                 All fields on a nested object");
        Console.WriteLine("    $this                    The value of the object itself");
        Console.WriteLine();
        Console.WriteLine("  Where predicate syntax:");
        Console.WriteLine("    field == 'value'         Equality");
        Console.WriteLine("    field != 'value'         Inequality");
        Console.WriteLine("    field > 'value'          Greater than");
        Console.WriteLine("    field >= 'value'         Greater than or equal");
        Console.WriteLine("    field < 'value'          Less than");
        Console.WriteLine("    field <= 'value'         Less than or equal");
        Console.WriteLine("    field =~ 'pattern'       Regex match (strings only)");
        Console.WriteLine("    ... and ...              Combine multiple predicates (all must match)");
        Console.WriteLine("    Values must be single-quoted. Use '' or \\' to escape quotes inside values.");
        Console.WriteLine();
        Console.WriteLine("  Supported field types: String, Boolean, Guid, DateTime, DateTimeOffset,");
        Console.WriteLine("    Int16, Int32, Int64, Double, Float, Class (address), Struct (address)");
        Console.WriteLine();
        Console.WriteLine("  Flags:");
        Console.WriteLine("    -short                   Print values only (no address headers or field names)");
        Console.WriteLine("    -debug                   Print debug/diagnostic output");
        Console.WriteLine();
        Console.WriteLine("  Examples:");
        Console.WriteLine("    tquery -mt 00007ff8a1234560 select _name,_id");
        Console.WriteLine("    tquery -mt 00007ff8a1234560 where _status == '1'");
        Console.WriteLine("    tquery -mt 00007ff8a1234560 select _name where _status > '0' and _active == 'True'");
        Console.WriteLine("    tquery -addr 0000020fa1234560 select *");
        Console.WriteLine("    tquery -array 0000020fa1234560 select _value where _key =~ 'foo.*'");
        Console.WriteLine("    tquery -implements MyNamespace.IMyInterface select _name");
        Console.WriteLine("    tquery -short -mt 00007ff8a1234560 select _name");
    }

    private static void PrintDecompileMethodHelp()
    {
        Console.WriteLine("decompilemethod -sp <address> | -ip <instructionPointer> | -md <methodDesc>");
        Console.WriteLine();
        Console.WriteLine("  Decompiles a managed method to C# source using ILSpy.");
        Console.WriteLine();
        Console.WriteLine("  Modes:");
        Console.WriteLine("    -sp <stackPointer>      Decompile the method at the given stack pointer.");
        Console.WriteLine("                            Finds the frame on any thread matching the SP,");
        Console.WriteLine("                            highlights the current source line based on the");
        Console.WriteLine("                            IL offset, prints surrounding stack frames, and");
        Console.WriteLine("                            lists objects found in the frame's local data.");
        Console.WriteLine("    -ip <instructionPtr>    Decompile the method containing the given native");
        Console.WriteLine("                            instruction pointer address.");
        Console.WriteLine("    -md <methodDesc>        Decompile the method identified by its MethodDesc");
        Console.WriteLine("                            address (from !dumpmd or !clrstack -md).");
        Console.WriteLine();
        Console.WriteLine("  Output (-sp mode):");
        Console.WriteLine("    - Method and type metadata tokens");
        Console.WriteLine("    - Surrounding stack frames (>> marks the current frame)");
        Console.WriteLine("    - Decompiled C# source with the executing line indicated");
        Console.WriteLine("    - Local objects on the stack frame (MethodTable, Address, Type)");
        Console.WriteLine();
        Console.WriteLine("  Examples:");
        Console.WriteLine("    decompilemethod -sp 00000045B71FE100");
        Console.WriteLine("    decompilemethod -ip 00007FF8A1C03B60");
        Console.WriteLine("    decompilemethod -md 00007FF8A1B54EA0");
    }

    private static void PrintDecompileTypeHelp()
    {
        Console.WriteLine("decompiletype [address] | -ad <address> | -nm <typeName> | -ip <instructionPtr> | -md <metadataToken> | -mt <methodTable>");
        Console.WriteLine();
        Console.WriteLine("  Decompiles an entire managed type to C# source using ILSpy.");
        Console.WriteLine();
        Console.WriteLine("  Modes:");
        Console.WriteLine("    <address>               Decompile the type of the object at the given");
        Console.WriteLine("                            address (shorthand for -ad).");
        Console.WriteLine("    -ad <address>           Decompile the type of the object at the given");
        Console.WriteLine("                            heap address.");
        Console.WriteLine("    -nm <typeName>          Decompile by fully-qualified type name");
        Console.WriteLine("                            (e.g. MyNamespace.MyClass).");
        Console.WriteLine("    -ip <instructionPtr>    Decompile the declaring type of the method");
        Console.WriteLine("                            containing the given instruction pointer.");
        Console.WriteLine("    -md <metadataToken>     Decompile the type identified by its metadata");
        Console.WriteLine("                            token (decimal or 0x hex).");
        Console.WriteLine("    -mt <methodTable>       Decompile the type identified by its method");
        Console.WriteLine("                            table address.");
        Console.WriteLine();
        Console.WriteLine("  Examples:");
        Console.WriteLine("    decompiletype 0000020FA1234560");
        Console.WriteLine("    decompiletype -ad 0000020FA1234560");
        Console.WriteLine("    decompiletype -nm System.Net.Http.HttpClient");
        Console.WriteLine("    decompiletype -ip 00007FF8A1C03B60");
        Console.WriteLine("    decompiletype -md 0x02000042");
        Console.WriteLine("    decompiletype -mt 00007FF8A1234560");
    }

    private static void PrintBuildObjectGraphHelp()
    {
        Console.WriteLine("buildobjectgraph");
        Console.WriteLine();
        Console.WriteLine("  Builds a whole-heap object reference graph from the target process or dump,");
        Console.WriteLine("  then computes a dominator tree (Lengauer-Tarjan) and retained sizes.");
        Console.WriteLine("  The results are cached in memory for use by subsequent commands.");
        Console.WriteLine();
        Console.WriteLine("  What it computes:");
        Console.WriteLine("    1. Object graph   — every managed object as a node, references as edges.");
        Console.WriteLine("    2. Dominator tree  — node A dominates node B if every path from the GC");
        Console.WriteLine("                         roots to B passes through A. If A were collected,");
        Console.WriteLine("                         everything it dominates would also be collected.");
        Console.WriteLine("    3. Retained sizes  — per-type minimum retained bytes, computed from the");
        Console.WriteLine("                         dominator tree.");
        Console.WriteLine();
        Console.WriteLine("  Output after build:");
        Console.WriteLine("    Nodes, Edges, Types, Total Size (bytes), Retained summaries count.");
        Console.WriteLine();
        Console.WriteLine("  Required before running:");
        Console.WriteLine("    retainedbytestat, referredfrom, refferedtotree");
        Console.WriteLine();
        Console.WriteLine("  Note: This can be slow and memory-intensive on large heaps.");
    }

    private static void PrintRetainedByteStatHelp()
    {
        Console.WriteLine("retainedbytestat [--min-retained-bytes N]");
        Console.WriteLine();
        Console.WriteLine("  Displays per-type retained byte statistics from the dominator tree.");
        Console.WriteLine("  Requires: buildobjectgraph");
        Console.WriteLine();
        Console.WriteLine("  For each type, shows the minimum retained bytes (the memory that would");
        Console.WriteLine("  become collectible if all instances of that type were removed) and the");
        Console.WriteLine("  exclusive (shallow) bytes owned directly by instances of that type.");
        Console.WriteLine();
        Console.WriteLine("  Options:");
        Console.WriteLine("    --min-retained-bytes N   Only show types retaining at least N bytes.");
        Console.WriteLine("                             Default: 1048576 (1 MB).");
        Console.WriteLine();
        Console.WriteLine("  Output columns:");
        Console.WriteLine("    Min Retained  — minimum retained bytes for the type");
        Console.WriteLine("    Bytes         — exclusive (shallow) size of all instances");
        Console.WriteLine("    Type          — fully-qualified type name");
        Console.WriteLine();
        Console.WriteLine("  Results are sorted by Min Retained descending, then Bytes descending.");
        Console.WriteLine();
        Console.WriteLine("  Examples:");
        Console.WriteLine("    retainedbytestat");
        Console.WriteLine("    retainedbytestat --min-retained-bytes 0");
        Console.WriteLine("    retainedbytestat --min-retained-bytes 10000000");
    }

    private static void PrintReferredFromHelp()
    {
        Console.WriteLine("referredfrom <TypeName> [--top N]");
        Console.WriteLine();
        Console.WriteLine("  Shows which parent types hold direct references to instances of the");
        Console.WriteLine("  given type, ranked by total referenced bytes.");
        Console.WriteLine("  Requires: buildobjectgraph");
        Console.WriteLine();
        Console.WriteLine("  Finds all reachable objects matching <TypeName>, then walks their");
        Console.WriteLine("  incoming references (parents in the object graph) and aggregates");
        Console.WriteLine("  by parent type.");
        Console.WriteLine();
        Console.WriteLine("  Type matching:");
        Console.WriteLine("    First attempts an exact (case-insensitive) match on the fully-qualified");
        Console.WriteLine("    type name. If no exact match is found, falls back to a substring");
        Console.WriteLine("    (contains) match.");
        Console.WriteLine();
        Console.WriteLine("  Options:");
        Console.WriteLine("    --top N    Number of parent types to show. Default: 10.");
        Console.WriteLine();
        Console.WriteLine("  Output columns:");
        Console.WriteLine("    Bytes       — total shallow size of matched objects held by this parent type");
        Console.WriteLine("    Count       — number of references from this parent type");
        Console.WriteLine("    ParentType  — fully-qualified parent type name");
        Console.WriteLine();
        Console.WriteLine("  Examples:");
        Console.WriteLine("    referredfrom System.String");
        Console.WriteLine("    referredfrom System.Byte[] --top 20");
        Console.WriteLine("    referredfrom MyApp.Models.Customer");
    }

    private static void PrintRefferedToTreeHelp()
    {
        Console.WriteLine("refferedtotree <TypeName> [--levels N]");
        Console.WriteLine();
        Console.WriteLine("  Prints a tree of outgoing references from instances of the given type,");
        Console.WriteLine("  expanding child types level by level.");
        Console.WriteLine("  Requires: buildobjectgraph");
        Console.WriteLine();
        Console.WriteLine("  Starting from all reachable objects matching <TypeName>, walks outgoing");
        Console.WriteLine("  references in the object graph, aggregating children by type at each");
        Console.WriteLine("  level. Avoids cycles by tracking visited nodes across the path.");
        Console.WriteLine();
        Console.WriteLine("  Type matching:");
        Console.WriteLine("    First attempts an exact (case-insensitive) match on the fully-qualified");
        Console.WriteLine("    type name. If no exact match is found, falls back to a substring");
        Console.WriteLine("    (contains) match.");
        Console.WriteLine();
        Console.WriteLine("  Options:");
        Console.WriteLine("    --levels N   Depth of the tree to expand. Default: 3.");
        Console.WriteLine();
        Console.WriteLine("  Output columns:");
        Console.WriteLine("    Bytes  — total shallow size of child objects at this level");
        Console.WriteLine("    Refs   — number of child references at this level");
        Console.WriteLine("    Type   — child type name (indented by depth)");
        Console.WriteLine();
        Console.WriteLine("  Results at each level are sorted by Bytes descending, then Refs descending.");
        Console.WriteLine();
        Console.WriteLine("  Examples:");
        Console.WriteLine("    refferedtotree System.Net.Http.HttpClient");
        Console.WriteLine("    refferedtotree MyApp.Cache.CacheEntry --levels 5");
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

    private static readonly ThreadPool _threadPool = new(new ConcurrentQueue());
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
    
    [UnmanagedCallersOnly(EntryPoint = "tsemaphore", CallConvs = new[] { typeof(CallConvStdcall) })]
    public static int tsemaphore(nint pUnknown, nint args)
    {
        return _tsemaphore(pUnknown, args);
    }

    private static readonly SemaphoreCommandRunner _semaphoreRunner = new();
    private static int _tsemaphore(nint pUnknown, nint args)
    {
        try
        {
            SemaphoreCommand cmd = new(_semaphoreRunner, pUnknown);
            string? arguments = Marshal.PtrToStringAnsi(args);
            cmd.Run(arguments ?? "");
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"Failed to run {nameof(SemaphoreCommand)} command.");
            Console.Error.WriteLine(e);
        }

        return 0;
    }
    
    [UnmanagedCallersOnly(EntryPoint = "tquery", CallConvs = new[] { typeof(CallConvStdcall) })]
    public static int tquery(nint pUnknown, nint args)
    {
        return _tquery(pUnknown, args);
    }

    private static readonly QueryCommand.QueryRunner _queryRunner = new();
    private static int _tquery(nint pUnknown, nint args)
    {
        try
        {
            QueryCommand cmd = new(_queryRunner, pUnknown);
            string? arguments = Marshal.PtrToStringAnsi(args);
            cmd.Run(arguments ?? "");
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"Failed to run {nameof(QueryCommand)} command.");
            Console.Error.WriteLine(e);
        }

        return 0;
    }
    
    [UnmanagedCallersOnly(EntryPoint = "tstore", CallConvs = new[] { typeof(CallConvStdcall) })]
    public static int tstore(nint pUnknown, nint args)
    {
        return _tstore(pUnknown, args);
    }

    private static int _tstore(nint pUnknown, nint args)
    {
        try
        {
            StoreCommand cmd = new (pUnknown);
            string? arguments = Marshal.PtrToStringAnsi(args);
            cmd.Run(arguments ?? "");
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"Failed to run {nameof(StoreCommand)} command.");
            Console.Error.WriteLine(e);
        }
        
        return 0;
    }

    [UnmanagedCallersOnly(EntryPoint = "types", CallConvs = new[] { typeof(CallConvStdcall) })]
    public static int types(nint pUnknown, nint args)
    {
        return _types(pUnknown, args);
    }

    private static int _types(nint pUnknown, nint args)
    {
        try
        {
            TypesCommand cmd = new (pUnknown);
            string? arguments = Marshal.PtrToStringAnsi(args);
            cmd.Run(arguments ?? "");
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"Failed to run {nameof(TypesCommand)} command");
            Console.Error.WriteLine(e);
        }
        
        return 0;
    }

    [UnmanagedCallersOnly(EntryPoint = "inclusivebytes", CallConvs = new[] { typeof(CallConvStdcall) })]
    public static int inclusivebytes(nint pUnknown, nint args)
    {
        return _inclusivebytes(pUnknown, args);
    }

    private static int _inclusivebytes(nint pUnknown, nint args)
    {
        try
        {
            InclusiveBytesCommand cmd = new(pUnknown);
            string? arguments = Marshal.PtrToStringAnsi(args);
            cmd.Run(arguments ?? "");
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"Failed to run {nameof(InclusiveBytesCommand)} command");
            Console.Error.WriteLine(e);
        }

        return 0;
    }

    [UnmanagedCallersOnly(EntryPoint = "buildmemorygraph", CallConvs = new[] { typeof(CallConvStdcall) })]
    public static int BuildMemoryGraph(nint pUnknown, nint args)
    {
        try
        {
            BuildMemoryGraphCommand cmd = new(pUnknown);
            string? arguments = Marshal.PtrToStringAnsi(args);
            cmd.Run(arguments ?? "");
        }
        catch (Exception e)
        {
            Console.Error.WriteLine("Failed to run buildmemorygraph command.");
            Console.Error.WriteLine(e);
        }

        return 0;
    }

    [UnmanagedCallersOnly(EntryPoint = "buildobjectgraph", CallConvs = new[] { typeof(CallConvStdcall) })]
    public static int BuildObjectGraph(nint pUnknown, nint args)
    {
        try
        {
            BuildObjectGraphCommand cmd = new(pUnknown);
            string? arguments = Marshal.PtrToStringAnsi(args);
            cmd.Run(arguments ?? "");
        }
        catch (Exception e)
        {
            Console.Error.WriteLine("Failed to run buildobjectgraph command.");
            Console.Error.WriteLine(e);
        }

        return 0;
    }

    [UnmanagedCallersOnly(EntryPoint = "retainedbytestat", CallConvs = new[] { typeof(CallConvStdcall) })]
    public static int RetainedByteStat(nint pUnknown, nint args)
    {
        try
        {
            RetainedByteStatCommand cmd = new(pUnknown);
            string? arguments = Marshal.PtrToStringAnsi(args);
            cmd.Run(arguments ?? "");
        }
        catch (Exception e)
        {
            Console.Error.WriteLine("Failed to run retainedbytestat command.");
            Console.Error.WriteLine(e);
        }

        return 0;
    }

    [UnmanagedCallersOnly(EntryPoint = "referredfrom", CallConvs = new[] { typeof(CallConvStdcall) })]
    public static int ReferredFrom(nint pUnknown, nint args)
    {
        try
        {
            ReferredFromCommand cmd = new(pUnknown);
            string? arguments = Marshal.PtrToStringAnsi(args);
            cmd.Run(arguments ?? "");
        }
        catch (Exception e)
        {
            Console.Error.WriteLine("Failed to run referredfrom command.");
            Console.Error.WriteLine(e);
        }

        return 0;
    }

    [UnmanagedCallersOnly(EntryPoint = "refferedtotree", CallConvs = new[] { typeof(CallConvStdcall) })]
    public static int RefferedToTree(nint pUnknown, nint args)
    {
        try
        {
            RefferedToTreeCommand cmd = new(pUnknown);
            string? arguments = Marshal.PtrToStringAnsi(args);
            cmd.Run(arguments ?? "");
        }
        catch (Exception e)
        {
            Console.Error.WriteLine("Failed to run refferedtotree command.");
            Console.Error.WriteLine(e);
        }

        return 0;
    }

    [UnmanagedCallersOnly(EntryPoint = "totaltypebytes", CallConvs = new[] { typeof(CallConvStdcall) })]
    public static int TotalTypeBytes(nint pUnknown, nint args)
    {
        try
        {
            TotalTypeBytesCommand cmd = new(pUnknown);
            string? arguments = Marshal.PtrToStringAnsi(args);
            cmd.Run(arguments ?? "");
        }
        catch (Exception e)
        {
            Console.Error.WriteLine("Failed to run totaltypebytes command.");
            Console.Error.WriteLine(e);
        }

        return 0;
    }

    [UnmanagedCallersOnly(EntryPoint = "typebytes", CallConvs = new[] { typeof(CallConvStdcall) })]
    public static int TypeBytes(nint pUnknown, nint args)
    {
        try
        {
            TypeBytesCommand cmd = new(pUnknown);
            string? arguments = Marshal.PtrToStringAnsi(args);
            cmd.Run(arguments ?? "");
        }
        catch (Exception e)
        {
            Console.Error.WriteLine("Failed to run typebytes command.");
            Console.Error.WriteLine(e);
        }

        return 0;
    }
}
