using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Diagnostics.Runtime;

namespace HeapStat
{
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
}