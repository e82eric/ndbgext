using DbgEngExtension;
using Microsoft.Diagnostics.Runtime;

namespace ndbgext;

public class ThreadRefsCommand : DbgEngCommand
{
    private readonly ThreadRefsProvider _provider;

    public ThreadRefsCommand(ThreadRefsProvider provider, nint pUnknown, bool redirectConsoleOutput = true)
        : base(pUnknown, redirectConsoleOutput)
    {
        _provider = provider;
    }

    public void Run(string args)
    {
        var splitArgs = args.Split(' ');
        if (splitArgs.Length == 1 && Helper.TryParseAddress(splitArgs[0], out var address))
        {
            foreach (var runtime in Runtimes)
            {
                _provider.Run(runtime, address);
            }
        }
    }
    
}

public class ThreadRefsProvider
{
    public void Run(ClrRuntime runtime, ulong address)
    {
        var alreadyPrinted = new HashSet<string>();
        foreach (var clrThread in runtime.Threads)
        {
            ClrStackFrame? previousFrame = null;
            foreach (var frame in clrThread.EnumerateStackTrace())
            {
                if (previousFrame != null)
                {
                    ulong frameBase = frame.StackPointer;
                    ulong frameLimit = previousFrame.StackPointer;
                    foreach (var pointer in runtime.EnumeratePointersInRange(frameBase, frameLimit))
                    {
                        if (runtime.DataTarget.DataReader.ReadPointer(pointer, out var value))
                        {
                            if (value == address)
                            {
                                var toPrint = string.Format("OsId: {0:X} {1}.{2} {3:X} {4:X}", clrThread.OSThreadId,
                                    previousFrame.Method?.Type.Name, previousFrame.Method?.Name, frameBase, frameLimit);
                                if (!alreadyPrinted.Contains(toPrint))
                                {
                                    Console.WriteLine(toPrint);
                                    alreadyPrinted.Add(toPrint);
                                }
                            }
                        }
                    }
                }

                previousFrame = frame;
            }
        }
    }
}