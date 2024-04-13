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
        Run();
    }

    private void Run()
    {
        foreach (ClrRuntime runtime in Runtimes)
        {
            // Walk each thread in the process.
            foreach (ClrThread thread in runtime.Threads)
            {
                if (!thread.IsAlive)
                    continue;

                ClrException? currException = thread.CurrentException;
                if (currException is ClrException ex)
                    Console.WriteLine("Exception: {0:X} ({1}), HRESULT={2:X}", ex.Address, ex.Type.Name, ex.HResult);

                // Console.WriteLine("Thread {0:X}:", thread.OSThreadId);
                ClrStackFrame firstManagedFrame = null;
                foreach (var frame in thread.EnumerateStackTrace())
                {
                    if (frame.Kind == ClrStackFrameKind.ManagedMethod)
                    {
                        firstManagedFrame = frame;
                        break;
                    }
                }

                if (firstManagedFrame != null)
                {
                    Console.WriteLine("{0:X} {1}.{2}", thread.OSThreadId, firstManagedFrame.Method?.Type?.Name, firstManagedFrame.Method?.Name);
                }
                else
                {
                    Console.WriteLine("{0:X} No Managed Frame", thread.OSThreadId);
                }

            }
            Console.WriteLine();
            Console.WriteLine("----------------------------------");
            Console.WriteLine();
        }
    }
}