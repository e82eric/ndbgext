using DbgEngExtension;
using Microsoft.Diagnostics.Runtime;

namespace ndbgext;

public class ClrUniqStack : DbgEngCommand
{
    public ClrUniqStack(nint pUnknown, bool redirectConsoleOutput = true)
        : base(pUnknown, redirectConsoleOutput)
    {
    }

    public ClrUniqStack(IDisposable dbgeng, bool redirectConsoleOutput = false)
        : base(dbgeng, redirectConsoleOutput)
    {
    }

    internal void Run(string args)
    {
        Run();
    }

    public void Run()
    {
        Dictionary<string, (int Count, ulong TotalSize)> sizes = new();
        List<(List<int> metadataTokens, List<ClrThread> threads)> uniqueStacks = new();

        // DbgEngCommand has helper properties for DataTarget and all ClrRuntimes:
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
                            Console.WriteLine($"    {frame.StackPointer:x12} {frame.InstructionPointer:x12} {frame.FrameName} {method?.Type?.Name}.{method?.Name} {method?.MetadataToken}");
                        }
                    }

                    Console.WriteLine($"Number of threads: {uniqueStack.threads.Count}");
                    Console.WriteLine($"Threads: {string.Join(',', uniqueStack.threads.Select(t => $"0x{t.OSThreadId:X} ({t.ManagedThreadId})"))}");

                    Console.WriteLine();
                    Console.WriteLine("----------------------------------");
                    Console.WriteLine();
                }
            }
        }
    }
}