using System.Globalization;
using DbgEngExtension;
using Microsoft.Diagnostics.Runtime;

namespace ndbgext;

public class DecompileCurrentFrameCommand : DbgEngCommand
{
    private readonly DecompileCurrentFrameProvider _currentFrameProvider;

    public DecompileCurrentFrameCommand(DecompileCurrentFrameProvider currentFrameProvider, nint pUnknown, bool redirectConsoleOutput = true)
        : base(pUnknown, redirectConsoleOutput)
    {
        _currentFrameProvider = currentFrameProvider;
    }

    internal void Run(string args)
    {
        var arguments = args.Split(' ');
        if (arguments.Length == 2 && arguments[0] == "-ip")
        {
            if(Helper.TryParseAddress(arguments[1], out var parsedInstructionPointer))
            {
                foreach (var runtime in Runtimes)
                {
                    _currentFrameProvider.DecompileMethod(runtime, parsedInstructionPointer);
                }
            }
            else
            {
                Console.WriteLine("Could not parse {0} as address", arguments[1]);
            }

            return;
        }
        if (arguments.Length != 1)
        {
            Console.WriteLine("missing instruction pointer address");
        }
        
        if(Helper.TryParseAddress(arguments[0], out var parsedStackPointer))
        {
            foreach (var runtime in Runtimes)
            {
                _currentFrameProvider.Run(runtime, parsedStackPointer);
            }
        }
        else
        {
            Console.WriteLine("Could not parse {0} as address", arguments[0]);
        }
    }
}

public class DecompileCurrentFrameProvider
{
    private readonly Decompiler _decompiler;

    public DecompileCurrentFrameProvider(Decompiler decompiler)
    {
        _decompiler = decompiler;
    }
    public void Run(ClrRuntime runtime, ulong stackPointer)
    {
        ClrStackFrame? nextFrame = null;
        ClrStackFrame? currentFrame = null;
        ClrStackFrame? previousFrame = null;
        foreach (var clrThread in runtime.Threads)
        {
            using (var stackTraceEnumerator = clrThread.EnumerateStackTrace().GetEnumerator())
            {
                while (stackTraceEnumerator.MoveNext())
                {
                    var frame = stackTraceEnumerator.Current;
                    if (frame.StackPointer == stackPointer)
                    {
                        currentFrame = frame;
                        if (stackTraceEnumerator.MoveNext())
                        {
                            previousFrame = stackTraceEnumerator.Current;
                        }
                        break;
                    }

                    nextFrame = frame;
                }

                if (currentFrame != null)
                {
                    break;
                }
            }
        }

        if (currentFrame?.Method != null)
        {
            var clrMethod = currentFrame.Method;
            Console.WriteLine("Method Token: {0:X}", clrMethod.MetadataToken);
            Console.WriteLine("Type Token: {0:X}", clrMethod.Type.MetadataToken);
            
            var ilOffsets = new List<int>();
            foreach (var ilInfo in clrMethod.ILOffsetMap)
            {
                if (ilInfo.StartAddress <= currentFrame.InstructionPointer && ilInfo.EndAddress >= currentFrame.InstructionPointer)
                {
                    if (ilInfo.ILOffset >= 0 && ilInfo.EndAddress - ilInfo.StartAddress > 0)
                    {
                        ilOffsets.Add(ilInfo.ILOffset);
                    }
                }
            }
            
            Console.WriteLine();
            PrintFrame(nextFrame, false);
            PrintFrame(currentFrame, true);
            PrintFrame(previousFrame, false);
            Console.WriteLine();
            var code = _decompiler.DecompileMethodWithCurrentLineIndicator(runtime, clrMethod, ilOffsets, nextFrame.Method?.Name);
            Console.WriteLine(code);

            if (previousFrame != null && currentFrame != null)
            {
                Console.WriteLine();
                Console.WriteLine("Frame data: {0:X} {1:X}", currentFrame.StackPointer, previousFrame.StackPointer);
                var locals = new List<Local>();
                foreach (var ptr in EnumeratePointersInRange(currentFrame.StackPointer, previousFrame.StackPointer,
                             runtime))
                {
                    if (runtime.DataTarget.DataReader.ReadPointer(ptr, out var value))
                    {
                        var objectType = runtime.Heap.GetObjectType(value);
                        if (objectType != null)
                        {
                            var local = new Local
                            {
                                Address = value, MethodTable = objectType.MethodTable, Type = objectType.Name
                            };
                            locals.Add(local);
                        }
                        else
                        {
                            locals.Add(new Local { Address = value });
                        }
                    }
                }

                var distinctLocals = locals.Distinct(new LocalComparer());
                foreach (var local in distinctLocals)
                {
                    Console.WriteLine("{0:X} {1:X} {2}", local.MethodTable, local.Address, local.Type);
                }
            }
        }
        else
        {
            Console.WriteLine("Could not file method frame/method for {0:X}", stackPointer);
        }
    }

    public void DecompileMethod(ClrRuntime runtime, ulong instructionPointer)
    {
        var method = runtime.GetMethodByInstructionPointer(instructionPointer);
        if (method != null)
        {
            var code = _decompiler.DecompileMethod(runtime, method);
            Console.WriteLine(code);
        }
    }

    private static void PrintFrame(ClrStackFrame? frame, bool isCurrent)
    {
        var symbol = isCurrent ? ">>" : "  ";
        Console.WriteLine($"{symbol} {frame?.StackPointer:x12} {frame?.InstructionPointer:x12} {frame?.FrameName} {frame?.Method?.Type.Name}.{frame?.Method?.Name}");
    }
    
    private IEnumerable<ulong> EnumeratePointersInRange(ulong start, ulong stop, ClrRuntime clr)
    {
        uint diff = (uint)clr.DataTarget.DataReader.PointerSize;

        if (start > stop)
            for (ulong ptr = stop; ptr <= start; ptr += diff)
                yield return ptr;
        else
            for (ulong ptr = stop; ptr >= start; ptr -= diff)
                yield return ptr;
    }

    struct Local
    {
        public ulong MethodTable;
        public ulong Address;
        public string Type;
    }

    private class LocalComparer : IEqualityComparer<Local>
    {
        public bool Equals(Local x, Local y)
        {
            if (ReferenceEquals(x, y)) return true;

            if (ReferenceEquals(x, null) || ReferenceEquals(y, null))
                return false;

            return x.Address == y.Address;
        }

        public int GetHashCode(Local local)
        {
            return local.Address.GetHashCode();
        }
    }
}