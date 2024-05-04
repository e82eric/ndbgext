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
        var instructionpointer = arguments[0];
        if (instructionpointer.StartsWith("0x"))
        {
            // remove "0x" for parsing
            instructionpointer = instructionpointer.Substring(2).TrimStart('0');
        }

        // remove the leading 0000 that WinDBG often add in 64 bit
        instructionpointer = instructionpointer.TrimStart('0');

        if (ulong.TryParse(instructionpointer, System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out var parsedInstructionPointer))
        {
            foreach (var runtime in Runtimes)
            {
                _provider.Run(runtime, parsedInstructionPointer);
            }
        }
        else
        {
            Console.WriteLine("Could not parse {0} as address", instructionpointer);
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
    public void Run(ClrRuntime runtime, ulong instructionPointer)
    {
        ClrMethod? nextClrMethod = null;
        ClrMethod? clrMethod = null;
        foreach (var clrThread in runtime.Threads)
        {
            foreach (var frame in clrThread.EnumerateStackTrace())
            {
                if (frame.InstructionPointer == instructionPointer)
                {
                    clrMethod = frame.Method;
                    break;
                }

                nextClrMethod = frame.Method;
            }

            if (clrMethod != null)
            {
                break;
            }
        }

        if (clrMethod != null)
        {
            var ilOffset = clrMethod.GetILOffset(instructionPointer);
            var ilOffsets = new List<int>();
            foreach (var ilInfo in clrMethod.ILOffsetMap)
            {
                if (ilInfo.StartAddress <= instructionPointer && ilInfo.EndAddress >= instructionPointer)
                {
                    if (ilInfo.ILOffset >= 0 && ilInfo.EndAddress - ilInfo.StartAddress > 0)
                    {
                        ilOffsets.Add(ilInfo.ILOffset);
                    }
                }
            }
            
            Console.WriteLine("{0} {1} {2}", clrMethod.Name, clrMethod.MetadataToken, ilOffset);
            var code = _decompiler.Decompile(runtime, clrMethod.Type.Module.Name, clrMethod, ilOffsets, nextClrMethod?.Name);
            Console.WriteLine(code);
        }
        else
        {
            Console.WriteLine("Could not file method frame/method for {0:X}", instructionPointer);
        }
    }
}