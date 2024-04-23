using DbgEngExtension;
using Microsoft.Diagnostics.Runtime;

namespace ndbgext;

public class Threads : DbgEngCommand
{
    public Threads(nint pUnknown, bool redirectConsoleOutput = true)
        : base(pUnknown, redirectConsoleOutput)
    {
    }

    public Threads(IDisposable dbgeng, bool redirectConsoleOutput = false)
        : base(dbgeng, redirectConsoleOutput)
    {
    }

    internal void Run(string args)
    {
        var split = args.Split(' ');
        var details = split.Length > 0 && split[0] == "-details";
        Run(true, details);
    }

    private void Run(bool methodStats, bool details)
    {
        foreach (ClrRuntime runtime in Runtimes)
        {
            var threadsItems = GetThreadItems(runtime);

            if (methodStats)
            {
                var countByMethod = threadsItems .GroupBy(r => r.MetadataToken);

                Console.WriteLine("--------By Method stats");
                Console.WriteLine();
                foreach (var c in countByMethod)
                {
                    var first = c.FirstOrDefault();
                    if (first != null)
                    {
                        Console.WriteLine(first.MethodName);
                    }

                    var osThreadIs = c.Select(c => $"0x{c.OSThreadId:X}");
                    var concatenatedThreadIds = string.Join(',', osThreadIs);
                    Console.WriteLine("Threads: {0}", concatenatedThreadIds);
                    Console.WriteLine("Number of threads: {0}", osThreadIs.Count());
                    Console.WriteLine();
                }
            }

            if (details)
            {
                Console.WriteLine("--------Details");
                Console.WriteLine();
                foreach (var threadsItem in threadsItems)
                {
                    Console.WriteLine("{0:X}|{1}|{2}|{3}", threadsItem.OSThreadId, threadsItem.MethodName, threadsItem.LockCount, threadsItem.Exception);
                }
            }
        }
    }

    private IReadOnlyList<ThreadsItem> GetThreadItems(ClrRuntime runtime)
    {
        var result = new List<ThreadsItem>();
        foreach (ClrThread thread in runtime.Threads)
        {
            if (!thread.IsAlive)
                continue;

            ClrStackFrame firstManagedFrame = null;
            foreach (var frame in thread.EnumerateStackTrace())
            {
                if (frame.Kind == ClrStackFrameKind.ManagedMethod)
                {
                    firstManagedFrame = frame;
                    break;
                }
            }

            var firstManagedFrameStr = string.Empty;
            if (firstManagedFrame != null)
            {
                firstManagedFrameStr = string.Format("{0}.{1}", firstManagedFrame.Method?.Type?.Name, firstManagedFrame.Method?.Name);
            }
            else
            {
                firstManagedFrameStr = string.Format("No Managed Frame");
            }

            ClrException? currException = thread.CurrentException;
            var exceptionStr = string.Empty;
            if (currException is ClrException ex)
            {
                exceptionStr = string.Format("Exception: {0:X} ({1}), HRESULT={2:X}", ex.Address, ex.Type.Name, ex.HResult);
            }

            var item = new ThreadsItem
            {
                OSThreadId = thread.OSThreadId,
                LockCount = thread.LockCount,
                Exception = exceptionStr,
                MetadataToken = firstManagedFrame?.Method?.MetadataToken,
                MethodName = firstManagedFrameStr
            };

            result.Add(item);
        }

        return result;
    }
}

class ThreadsItem
{
    public uint OSThreadId { get; set; }
    public int? MetadataToken { get; set; }
    public string MethodName { get; set; }
    public uint LockCount { get; set; }
    public string Exception { get; set; }
}