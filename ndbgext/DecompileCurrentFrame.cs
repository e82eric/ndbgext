using System.Globalization;
using DbgEngExtension;
using Microsoft.Diagnostics.Runtime;

namespace ndbgext;

public class DecompileCommand : DbgEngCommand
{
    private readonly DecompileProvider _provider;

    public DecompileCommand(DecompileProvider provider, nint pUnknown, bool redirectConsoleOutput = true)
        : base(pUnknown, redirectConsoleOutput)
    {
        _provider = provider;
    }

    internal void Run(string args)
    {
        var arguments = args.Split(' ');
        if (arguments.Length != 1)
        {
            Console.WriteLine("missing instruction pointer address");
        }
        var stackPointer = arguments[0];
        if (stackPointer.StartsWith("0x"))
        {
            // remove "0x" for parsing
            stackPointer = stackPointer.Substring(2).TrimStart('0');
        }

        // remove the leading 0000 that WinDBG often add in 64 bit
        stackPointer = stackPointer.TrimStart('0');

        if (ulong.TryParse(stackPointer, NumberStyles.HexNumber,
                CultureInfo.InvariantCulture, out var parsedStackPointer))
        {
            foreach (var runtime in Runtimes)
            {
                _provider.Run(runtime, parsedStackPointer);
            }
        }
        else
        {
            Console.WriteLine("Could not parse {0} as address", stackPointer);
        }
    }
}

public class DecompileProvider
{
    private readonly Decompiler _decompiler;

    public DecompileProvider(Decompiler decompiler)
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
            var code = _decompiler.Decompile(runtime, clrMethod.Type.Module.Name, clrMethod, ilOffsets, nextFrame.Method?.Name);
            Console.WriteLine(code);

            Console.WriteLine();
            Console.WriteLine("Frame Locals: {0:X} {1:X}", currentFrame.StackPointer, previousFrame.StackPointer);
            var locals = new List<Local>();
            foreach (var ptr in EnumeratePointersInRange(currentFrame.StackPointer, previousFrame.StackPointer, runtime))
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
                }
            }

            var distinctLocals = locals.Distinct(new LocalComparer());
            foreach (var local in distinctLocals)
            {
                Console.WriteLine("{0:X} {1:X} {2}", local.MethodTable, local.Address, local.Type);
            }
        }
        else
        {
            Console.WriteLine("Could not file method frame/method for {0:X}", stackPointer);
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
    
    class LocalComparer : IEqualityComparer<Local>
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