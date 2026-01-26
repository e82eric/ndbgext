using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Microsoft.Diagnostics.Runtime;

namespace HeapStat
{
    internal static class Program
    {
        static bool TryParseAddress(string addressInHexa, out ulong address)
        {
            if (string.IsNullOrWhiteSpace(addressInHexa))
            {
                address = 0;
                return false;
            }

            // skip 0x or leading 0000 if needed
            if (addressInHexa.StartsWith("0x"))
            {
                addressInHexa = addressInHexa.Substring(2);
            }

            addressInHexa = addressInHexa.TrimStart('0');

            int index = addressInHexa.IndexOf('`');
            if (index >= 0 && index < addressInHexa.Length - 1)
            {
                // Remove up to one instance of ` since that's what WinDbg adds to its x64 addresses.
                addressInHexa = addressInHexa.Substring(0, index) + addressInHexa.Substring(index + 1);
            }

            return ulong.TryParse(addressInHexa, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out address);
        }
        
        public static int Main(string[] args)
        {
            if (args.Length == 0 || !int.TryParse(args[0], out int pid))
            {
                Console.Error.WriteLine("Usage: DumpHeapStatNetFx.exe <pid>");
                return 2;
            }

            string gcRootType = null;
            if (args.Length == 2)
            {
                gcRootType = args[1];
            }

            try
            {
                using (DataTarget target = DataTarget.AttachToProcess(pid, false))
                {
                    ClrRuntime runtime = target.ClrVersions[0].CreateRuntime();

                    if (gcRootType != null)
                    {
                        ClrObject? gcRootObj = null;
                        foreach (var obj in runtime.Heap.EnumerateObjects())
                        {
                            if (obj.Type?.Name == gcRootType)
                            {
                                gcRootObj = obj;
                                break;
                            }
                        }

                        if (gcRootObj != null)
                        {
                            var consoleService = new ConsoleService();
                            var gcRoot = new GCRootCommand(
                                new MemoryServiceFromDataReader(runtime.DataTarget.DataReader),
                                new RootCacheService(runtime, consoleService),
                                new StaticVariableService(runtime),
                                consoleService);
                            
                            gcRoot.TargetAddress = gcRootObj.Value.Address;
                            gcRoot.Invoke(runtime);
                            return 0;
                        }
                    }

                    var heap = runtime.Heap;
                    if (!heap.CanWalkHeap)
                    {
                        Console.Error.WriteLine("Cannot walk heap (heap.CanWalkHeap == false).");
                        return 4;
                    }

                    var stats = new Dictionary<ulong, TypeStat>(capacity: 8192);

                    foreach (var obj in heap.EnumerateObjects())
                    {
                        if (!obj.IsValid)
                        {
                            continue;
                        }

                        var type = obj.Type;
                        if (type == null)
                        {
                            continue;
                        }

                        ulong mt = type.MethodTable;
                        string name = type.Name ?? "<unknown>";
                        ulong size = obj.Size;

                        if (!stats.TryGetValue(mt, out var s))
                        {
                            s = new TypeStat(mt, name);
                            stats.Add(mt, s);
                        }

                        s.Count++;
                        s.TotalSize += (long)size;
                    }

                    var ordered = stats.Values
                        .OrderByDescending(s => s.TotalSize)
                        .ThenByDescending(s => s.Count)
                        .ToList();

                    Console.WriteLine("{0,16} {1,10} {2,12} {3}",
                        "MT", "Count", "TotalSize", "Type");

                    foreach (var s in ordered)
                    {
                        Console.WriteLine("{0,16:X} {1,10:N0} {2,12:N0} {3}",
                            s.MethodTable, s.Count, s.TotalSize, s.TypeName);
                    }

                    long totalObjects = ordered.Sum(s => s.Count);
                    long totalBytes = ordered.Sum(s => s.TotalSize);

                    Console.WriteLine();
                    Console.WriteLine("Total Objects: {0:N0}", totalObjects);
                    Console.WriteLine("Total Bytes:   {0:N0}", totalBytes);
                    
                    Console.WriteLine();
                    Console.WriteLine("Threads:");
                    SosLikeThreads.Dump(runtime);
                    
                    Console.WriteLine();
                    Console.WriteLine("Unique call stacks");
                    new ClrUniqStack().Run(new []{runtime});
                    
                    Console.WriteLine();
                    Console.WriteLine("Task stacks");
                    new DumpAsyncCommand().Execute(new List<ClrRuntime>(){runtime});
                }

                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                return 1;
            }
        }

        private sealed class TypeStat
        {
            public ulong MethodTable { get; }
            public string TypeName { get; }
            public long Count { get; set; }
            public long TotalSize { get; set; }

            public TypeStat(ulong mt, string name)
            {
                MethodTable = mt;
                TypeName = name;
            }
        }
    }
    
    public class ClrUniqStack
    {
        public void Run(IEnumerable<ClrRuntime> runtimes)
        {
            List<(List<int> metadataTokens, List<ClrThread> threads)> uniqueStacks = new List<(List<int> metadataTokens, List<ClrThread> threads)>();

            foreach (ClrRuntime runtime in runtimes)
            {
                foreach (ClrThread thread in runtime.Threads)
                {
                    if (!thread.IsAlive)
                        continue;
                
                    var metadataTokens = new List<int>();
                    foreach (ClrStackFrame frame in thread.EnumerateStackTrace())
                    {
                        if (frame.Method != null)
                        {
                            metadataTokens.Add(frame.Method.MetadataToken);
                        }
                    }

                    bool found = false;
                    foreach (var uniqueStack in uniqueStacks)
                    {
                        if (metadataTokens.Count == uniqueStack.metadataTokens.Count)
                        {
                            var allTokensMatch = true;
                            for (var i = 0; i < metadataTokens.Count; i++)
                            {
                                if (metadataTokens[i] != uniqueStack.metadataTokens[i])
                                {
                                    allTokensMatch = false;
                                    break;
                                }
                            }

                            if (allTokensMatch)
                            {
                                uniqueStack.threads.Add(thread);
                                found = true;
                            }
                        }
                    }

                    if (found == false)
                    {
                        uniqueStacks.Add((metadataTokens, new List<ClrThread> {thread}));
                    }
                }
            
                uniqueStacks.OrderBy(u => u.threads.Count);

                foreach (var uniqueStack in uniqueStacks)
                {
                    var firstThread = uniqueStack.threads.FirstOrDefault();
                    if (firstThread != null)
                    {
                        foreach (var frame in firstThread.EnumerateStackTrace())
                        {
                            if (frame.Kind == ClrStackFrameKind.ManagedMethod)
                            {
                                var method = frame.Method;
                                Console.WriteLine($"    {frame.StackPointer:x12} {frame.InstructionPointer:x12} {frame.FrameName} {method?.Type.Name}.{method?.Name} {method?.MetadataToken}");
                            }
                        }

                        Console.WriteLine($"Number of threads: {uniqueStack.threads.Count}");
                        Console.WriteLine($"Threads: {string.Join(",", uniqueStack.threads.Select(t => $"0x{t.OSThreadId:X} ({t.ManagedThreadId})"))}");

                        Console.WriteLine();
                        Console.WriteLine("----------------------------------");
                        Console.WriteLine();
                    }
                }
            }
        }
    }
    
    static class Utilities
    {
        internal static ClrObject TryGetObjectField(this ClrObject clrObject, string fieldName)
        {
            if (!clrObject.IsNull)
            {
                ClrInstanceField field = clrObject.Type?.GetFieldByName(fieldName);
                if (field is object && field.IsObjectReference)
                {
                    return field.ReadObject(clrObject.Address, interior: false);
                }
            }

            return default(ClrObject);
        }
        
        internal static IEnumerable<ClrObject> GetObjectsOfType(ClrHeap heap, string typeName)
        {
            return heap.EnumerateObjects()
                .Where(obj => string.Equals(obj.Type?.Name, typeName, StringComparison.Ordinal));
        }
        
        internal static ClrObject TryGetObjectField(this ClrValueType? clrObject, string fieldName)
        {
            if (clrObject is object)
            {
                ClrInstanceField field = clrObject.Value.Type?.GetFieldByName(fieldName);
                if (field is object && field.IsObjectReference)
                {
                    return clrObject.Value.ReadObjectField(fieldName);
                }
            }

            return default(ClrObject);
        }
        
        
        internal static ClrValueType? TryGetValueClassField(this ClrValueType? clrObject, string fieldName)
        {
            if (clrObject.HasValue)
            {
                ClrInstanceField field = clrObject.Value.Type?.GetFieldByName(fieldName);
                if (field is object && field.IsValueType)
                {
                    return clrObject.Value.ReadValueTypeField(fieldName);
                }
            }

            return null;
        }
        
        internal static ClrValueType? TryGetValueClassField(this ClrObject clrObject, string fieldName)
        {
            if (!clrObject.IsNull)
            {
                ClrInstanceField field = clrObject.Type?.GetFieldByName(fieldName);
                if (field?.Type is object && field.Type.IsValueType)
                {
                    // System.Console.WriteLine("{0} {1:x} Field {2} {3} {4} {5}", clrObject.Type.Name, clrObject.Address, fieldName, field.Type.Name, field.Type.IsValueType, field.Type.IsRuntimeType);
                    return clrObject.ReadValueTypeField(fieldName);
                }
            }

            return null;
        }
    }
    
    public static class SosLikeThreads
    {
        public static void Dump(ClrRuntime runtime)
        {
            var threads = runtime.Threads.ToArray();

            int total = threads.Length;
            int alive = threads.Count(t => GetBool(t, "IsAlive"));
            int dead = total - alive;
            int background = threads.Count(t => GetBool(t, "IsBackground"));
            int unstarted = threads.Count(t => GetBool(t, "IsUnstarted"));
            int finalizer = threads.Count(t => GetBool(t, "IsFinalizer"));
            int gc = threads.Count(t => GetBool(t, "IsGC"));
            int threadpool = threads.Count(t => GetBool(t, "IsThreadpoolThread"));

            Console.WriteLine("ThreadCount: {0}", total);
            Console.WriteLine("Alive: {0}  Dead: {1}  Background: {2}  Unstarted: {3}  Finalizer: {4}  GC: {5}  Threadpool: {6}",
                alive, dead, background, unstarted, finalizer, gc, threadpool);
            Console.WriteLine();

            Console.WriteLine(" idx  OSID  ThreadOBJ        State     GC Mode      GC Alloc Context        Domain           Lock  Apt  Exception");
            Console.WriteLine(" ---- ----  --------------   --------  ----------   --------------------    --------------   ----  ---  ----------------");

            int idx = 0;
            foreach (var t in threads)
            {
                uint osid = GetUInt(t, "OSThreadId");
                ulong threadObj = GetULong(t, "Address");
                ulong state = GetULong(t, "State");
                string gcMode = GetEnumName(t, "GCMode") ?? "";
                string allocCtx = FormatAllocContext(t);
                ulong domainAddr = GetDomainAddress(t);
                int lockCount = GetInt(t, "LockCount");
                string apt = GetEnumName(t, "ApartmentState") ?? GetEnumName(t, "AptState") ?? "";
                string ex = FormatException(t);

                Console.WriteLine("{0,4} {1,4:X}  {2,14:X}   {3,8:X}  {4,-10}   {5,-20}  {6,14:X}   {7,4}  {8,-3}  {9}",
                    idx, osid, threadObj, state, gcMode, allocCtx, domainAddr, lockCount, apt, ex);

                idx++;
            }
        }

        private static ulong GetDomainAddress(ClrThread t)
        {
            object ad = GetObj(t, "CurrentAppDomain") ?? GetObj(t, "AppDomain");
            if (ad == null) return 0;
            return GetULong(ad, "Address");
        }

        private static string FormatAllocContext(ClrThread t)
        {
            object ctx = GetObj(t, "AllocationContext") ?? GetObj(t, "AllocContext") ?? GetObj(t, "GCAllocContext");
            if (ctx == null)
                return "00000000:00000000";

            ulong start = GetULong(ctx, "Start");
            ulong end = GetULong(ctx, "End");
            if (end == 0) end = GetULong(ctx, "Limit");

            return string.Format(CultureInfo.InvariantCulture, "{0:X}:{1:X}", start, end);
        }

        private static string FormatException(ClrThread t)
        {
            object ex = GetObj(t, "CurrentException") ?? GetObj(t, "LastThrownException");
            if (ex == null) return "";

            ulong addr = GetULong(ex, "Address");
            string type = GetString(ex, "Type") ?? GetString(ex, "TypeName");
            if (string.IsNullOrEmpty(type))
            {
                object typeObj = GetObj(ex, "Type");
                type = GetString(typeObj, "Name");
            }

            if (addr == 0 && string.IsNullOrEmpty(type)) return "";
            if (string.IsNullOrEmpty(type)) return string.Format(CultureInfo.InvariantCulture, "{0:X}", addr);

            return string.Format(CultureInfo.InvariantCulture, "{0:X} ({1})", addr, type);
        }

        // --- reflection helpers (for ClrMD version differences) ---

        private static object GetObj(object o, string prop)
        {
            if (o == null) return null;
            var p = o.GetType().GetProperty(prop, BindingFlags.Public | BindingFlags.Instance);
            return p != null ? p.GetValue(o, null) : null;
        }

        private static string GetString(object o, string prop)
        {
            if (o == null) return null;
            return GetObj(o, prop) as string;
        }

        private static bool GetBool(object o, string prop)
        {
            object v = GetObj(o, prop);
            return v is bool && (bool)v;
        }

        private static int GetInt(object o, string prop)
        {
            object v = GetObj(o, prop);
            if (v is int) return (int)v;
            if (v is uint) return unchecked((int)(uint)v);
            if (v is short) return (short)v;
            if (v is ushort) return (ushort)v;
            if (v is byte) return (byte)v;
            if (v is sbyte) return (sbyte)v;
            return 0;
        }

        private static uint GetUInt(object o, string prop)
        {
            object v = GetObj(o, prop);
            if (v is uint) return (uint)v;
            if (v is int) return unchecked((uint)(int)v);
            if (v is ushort) return (ushort)v;
            if (v is short) return unchecked((uint)(ushort)(short)v);
            return 0;
        }

        private static ulong GetULong(object o, string prop)
        {
            object v = GetObj(o, prop);
            if (v is ulong) return (ulong)v;
            if (v is long) return unchecked((ulong)(long)v);
            if (v is uint) return (uint)v;
            if (v is int) return unchecked((uint)(int)v);
            if (v is IntPtr) return unchecked((ulong)((IntPtr)v).ToInt64());
            return 0;
        }

        private static string GetEnumName(object o, string prop)
        {
            object v = GetObj(o, prop);
            if (v == null) return null;
            var t = v.GetType();
            return t.IsEnum ? v.ToString() : null;
        }
    }
}