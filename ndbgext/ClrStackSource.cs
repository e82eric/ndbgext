using DbgEngExtension;
using Microsoft.Diagnostics.Runtime;

namespace ndbgext;

public class ClrStackSourceCommand : DbgEngCommand
{
    private readonly ClrStackSourceProvider _provider;

    public ClrStackSourceCommand(ClrStackSourceProvider provider, nint pUnknown, bool redirectConsoleOutput = true)
        : base(pUnknown, redirectConsoleOutput)
    {
        _provider = provider;
    }

    internal void Run(string args)
    {
        uint? tid = null;
        var parts = string.IsNullOrWhiteSpace(args)
            ? Array.Empty<string>()
            : args.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        for (var i = 0; i < parts.Length; i++)
        {
            if (parts[i] == "-tid" && i + 1 < parts.Length)
            {
                if (Helper.TryParseAddress(parts[i + 1], out var parsed) && parsed <= uint.MaxValue)
                {
                    tid = (uint)parsed;
                    i++;
                }
                else
                {
                    Console.WriteLine("Could not parse '{0}' as an OS thread id (hex).", parts[i + 1]);
                    return;
                }

                continue;
            }

            Console.WriteLine("Unknown argument '{0}'. Usage: clrstacksource [-tid <osThreadIdHex>]", parts[i]);
            return;
        }

        foreach (var runtime in Runtimes)
        {
            _provider.Run(runtime, tid);
        }
    }
}

public class ClrStackSourceProvider
{
    private readonly Decompiler _decompiler;

    public ClrStackSourceProvider(Decompiler decompiler)
    {
        _decompiler = decompiler;
    }

    public void Run(ClrRuntime runtime, uint? osThreadIdFilter)
    {
        foreach (var thread in runtime.Threads)
        {
            if (!thread.IsAlive)
                continue;
            if (osThreadIdFilter.HasValue && thread.OSThreadId != osThreadIdFilter.Value)
                continue;

            Console.WriteLine("OS Thread Id: 0x{0:X} ({1})", thread.OSThreadId, thread.ManagedThreadId);
            Console.WriteLine();

            List<ClrStackFrame> frames;
            try
            {
                frames = thread.EnumerateStackTrace().ToList();
            }
            catch (Exception e)
            {
                Console.WriteLine("    <stack walk failed: {0}>", e.Message);
                Console.WriteLine("----------------------------------");
                Console.WriteLine();
                continue;
            }

            for (var i = 0; i < frames.Count; i++)
            {
                var frame = frames[i];
                var nextFrame = i > 0 ? frames[i - 1] : null;

                Console.WriteLine($"   {frame.StackPointer:x12} {frame.InstructionPointer:x12} {frame.FrameName} {frame.Method?.Type.Name}.{frame.Method?.Name}");

                if (frame.Method == null || frame.Kind != ClrStackFrameKind.ManagedMethod)
                {
                    Console.WriteLine("    <no decompiled source available>");
                    Console.WriteLine();
                    continue;
                }

                var ilOffset = GetILOffsetForNativeOffset(frame.Method, frame.InstructionPointer);
                var ilOffsets = new List<int> { ilOffset };

                try
                {
                    var code = _decompiler.DecompileMethodWithCurrentLineIndicator(
                        runtime, frame.Method, ilOffsets, nextFrame?.Method?.Name);
                    Console.WriteLine(code);
                }
                catch (Exception e)
                {
                    Console.WriteLine("    <decompile failed: {0}>", e.Message);
                }

                Console.WriteLine();
            }

            Console.WriteLine("----------------------------------");
            Console.WriteLine();
        }
    }

    private static int GetILOffsetForNativeOffset(ClrMethod method, ulong ip)
    {
        var ilmap = method.ILOffsetMap;
        if (ilmap.IsDefaultOrEmpty)
            return -1;

        (ulong Distance, int Offset) closest = (ulong.MaxValue, -1);
        foreach (var entry in ilmap)
        {
            ulong distance;
            if (ip < entry.StartAddress) distance = entry.StartAddress - ip;
            else if (ip > entry.EndAddress) distance = ip - entry.EndAddress;
            else return entry.ILOffset;

            if (distance < closest.Distance)
                closest = (distance, entry.ILOffset);
        }

        return closest.Offset;
    }
}
