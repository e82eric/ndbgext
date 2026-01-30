using System;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Microsoft.Diagnostics.Runtime;

namespace HeapStat
{
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