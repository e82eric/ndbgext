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
    }
}

public class DecompileProvider
{
    public void Run(ClrRuntime runtime, ulong instructionPointer)
    {
        ClrMethod clrMethod = null;
        foreach (var clrThread in runtime.Threads)
        {
            foreach (var frame in clrThread.EnumerateStackTrace())
            {
                if (frame.InstructionPointer == instructionPointer)
                {
                    clrMethod = frame.Method;
                    break;
                }
            }

            if (clrMethod != null)
            {
                break;
            }
        }

        if (clrMethod != null)
        {
            var ilOffset = clrMethod.GetILOffset(instructionPointer);
            Console.WriteLine("{0} {1} {2}", clrMethod.Name, clrMethod.MetadataToken, ilOffset);
        }
    }
}