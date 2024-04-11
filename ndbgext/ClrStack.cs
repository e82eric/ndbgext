using DbgEngExtension;
using Microsoft.Diagnostics.Runtime;

namespace ndbgext;

public class ClrStack : DbgEngCommand
{
    /// <summary>
    /// Constructor.  It's assumed that this is the constructor used for a DbgEng plugin, since it
    /// will be handing us an IUnknown.  Therefore <paramref name="redirectConsoleOutput"/> defaults
    /// to true.
    /// </summary>
    /// <param name="pUnknown">The dbgeng instance we are interacting with.</param>
    /// <param name="redirectConsoleOutput">Whether to override Console.WriteLine and redirect it to DbgEng's output system.</param>
    public ClrStack(nint pUnknown, bool redirectConsoleOutput = true)
        : base(pUnknown, redirectConsoleOutput)
    {
    }

    /// <summary>
    /// Constructor.  It's assumed that this is the constructor used for a standalone DbgEng app, since it
    /// will be handing us a dbgeng object.  Therefore <paramref name="redirectConsoleOutput"/> defaults
    /// to false (don't modify Console.WriteLine, just write to the standard console).
    /// </summary>
    /// <param name="pUnknown">The dbgeng instance we are interacting with.</param>
    /// <param name="redirectConsoleOutput">Whether to override Console.WriteLine and redirect it to DbgEng's output system.</param>
    public ClrStack(IDisposable dbgeng, bool redirectConsoleOutput = false)
        : base(dbgeng, redirectConsoleOutput)
    {
    }

    // We don't want to expose the string parsing overload to other applications, they should call the actual
    // method with parameters.
    internal void Run(string args)
    {
        bool statOnly = args.Trim().Equals("-stat");
        Run(statOnly);
    }

    public void Run(bool statOnly)
    {
        Dictionary<string, (int Count, ulong TotalSize)> sizes = new();

        // DbgEngCommand has helper properties for DataTarget and all ClrRuntimes:
        foreach (ClrRuntime runtime in Runtimes)
        {
            // Walk each thread in the process.
            foreach (ClrThread thread in runtime.Threads)
            {
                // The ClrRuntime.Threads will also report threads which have recently died, but their
                // underlying datastructures have not yet been cleaned up.  This can potentially be
                // useful in debugging (!threads displays this information with XXX displayed for their
                // OS thread id).  You cannot walk the stack of these threads though, so we skip them
                // here.
                if (!thread.IsAlive)
                    continue;

                Console.WriteLine("Thread {0:X}:", thread.OSThreadId);
                Console.WriteLine("Stack: {0:X} - {1:X}", thread.StackBase, thread.StackLimit);

                // Each thread tracks a "last thrown exception".  This is the exception object which
                // !threads prints.  If that exception object is present, we will display some basic
                // exception data here.  Note that you can get the stack trace of the exception with
                // ClrHeapException.StackTrace (we don't do that here).
                ClrException? currException = thread.CurrentException;
                if (currException is ClrException ex)
                    Console.WriteLine("Exception: {0:X} ({1}), HRESULT={2:X}", ex.Address, ex.Type.Name, ex.HResult);

                // Walk the stack of the thread and print output similar to !ClrStack.
                Console.WriteLine();
                Console.WriteLine("Managed Callstack:");
                foreach (ClrStackFrame frame in thread.EnumerateStackTrace())
                {
                    // Note that CLRStackFrame currently only has three pieces of data: stack pointer,
                    // instruction pointer, and frame name (which comes from ToString).  Future
                    // versions of this API will allow you to get the type/function/module of the
                    // method (instead of just the name).  This is not yet implemented.
                    var method = frame.Method;
                    Console.WriteLine($"    {frame.StackPointer:x12} {frame.InstructionPointer:x12} {frame.FrameName} {method?.Name} {method?.MetadataToken}");
                }

                Console.WriteLine();
                Console.WriteLine("----------------------------------");
                Console.WriteLine();
            }
        }
    }
}