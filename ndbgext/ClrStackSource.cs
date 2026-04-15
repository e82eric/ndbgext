using System.Text;
using System.Text.RegularExpressions;
using System.Runtime.Versioning;
using DbgEngExtension;
using Microsoft.Diagnostics.Runtime;
using Microsoft.Diagnostics.Runtime.Utilities.DbgEng;

namespace ndbgext;

[SupportedOSPlatform("windows")]
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
        (int Start, int End)? frameRange = null;
        var includeFrameData = false;
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

            if (parts[i] == "-frames" && i + 1 < parts.Length)
            {
                if (TryParseFrameRange(parts[i + 1], out var parsedRange))
                {
                    frameRange = parsedRange;
                    i++;
                }
                else
                {
                    Console.WriteLine("Could not parse '{0}' as a frame range. Use start-end or a single frame number.", parts[i + 1]);
                    return;
                }

                continue;
            }

            if (parts[i] == "-frameData")
            {
                includeFrameData = true;
                continue;
            }

            Console.WriteLine("Unknown argument '{0}'. Usage: clrstacksource [-tid <osThreadIdHex>] [-frames <start-end>]", parts[i]);
            return;
        }

        if (!tid.HasValue)
        {
            if (!TryGetCurrentOsThreadId(out var currentThreadId))
            {
                Console.WriteLine("Could not determine the current debugger thread id. Use -tid to specify a thread explicitly.");
                return;
            }

            tid = currentThreadId;
        }

        foreach (var runtime in Runtimes)
        {
            _provider.Run(runtime, tid, frameRange, includeFrameData);
        }
    }

    private static bool TryParseFrameRange(string value, out (int Start, int End) range)
    {
        range = default;
        var parts = value.Split('-', 2, StringSplitOptions.TrimEntries);
        if (parts.Length == 1)
        {
            if (int.TryParse(parts[0], out var frame) && frame >= 0)
            {
                range = (frame, frame);
                return true;
            }

            return false;
        }

        if (parts.Length == 2
            && int.TryParse(parts[0], out var start)
            && int.TryParse(parts[1], out var end)
            && start >= 0
            && end >= start)
        {
            range = (start, end);
            return true;
        }

        return false;
    }

    private bool TryGetCurrentOsThreadId(out uint osThreadId)
    {
        osThreadId = 0;
        var outputCapture = new StringOutputCallbacks();
        var oldCaptures = DebugClient.GetOutputCallbacks();
        try
        {
            DebugClient.SetOutputCallbacks(outputCapture);
            var executeResult = DebugControl.Execute(
                DEBUG_OUTCTL.NOT_LOGGED | DEBUG_OUTCTL.THIS_CLIENT,
                "~.",
                DEBUG_EXECUTE.DEFAULT);

            if (executeResult != 0)
                return false;

            var output = outputCapture.Result();
            var match = Regex.Match(output, @"Id:\s*[0-9a-fA-F]+\.([0-9a-fA-F]+)", RegexOptions.CultureInvariant);
            return match.Success
                   && uint.TryParse(match.Groups[1].Value, System.Globalization.NumberStyles.HexNumber,
                       System.Globalization.CultureInfo.InvariantCulture, out osThreadId);
        }
        finally
        {
            DebugClient.SetOutputCallbacks(oldCaptures);
        }
    }

    private sealed class StringOutputCallbacks : IDebugOutputCallbacks
    {
        private readonly StringBuilder _sb = new();

        public string Result() => _sb.ToString();

        public void OnText(DEBUG_OUTPUT flags, string? text, ulong args)
        {
            _sb.Append(text);
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

    public void Run(ClrRuntime runtime, uint? osThreadIdFilter, (int Start, int End)? frameRange, bool includeFrameData)
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

            var startFrame = frameRange?.Start ?? 0;
            var endFrame = frameRange?.End ?? (frames.Count - 1);
            if (startFrame >= frames.Count)
            {
                Console.WriteLine("    <requested frame range {0}-{1} is outside the stack (0-{2})>", startFrame, endFrame, frames.Count - 1);
                Console.WriteLine("----------------------------------");
                Console.WriteLine();
                continue;
            }

            endFrame = Math.Min(endFrame, frames.Count - 1);

            for (var i = 0; i < frames.Count; i++)
            {
                var frame = frames[i];
                var nextFrame = i > 0 ? frames[i - 1] : null;
                var previousFrame = i + 1 < frames.Count ? frames[i + 1] : null;
                var inRange = i >= startFrame && i <= endFrame;

                Console.WriteLine($"[{i}] {frame.StackPointer:x12} {frame.InstructionPointer:x12} {frame.FrameName} {frame.Method?.Type.Name}.{frame.Method?.Name}");

                if (!inRange)
                {
                    Console.WriteLine();
                    continue;
                }

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
                    Helper.WritePlainText(code);
                }
                catch (Exception e)
                {
                    Console.WriteLine("    <decompile failed: {0}>", e.Message);
                }

                if (includeFrameData)
                {
                    PrintFrameData(runtime, frame, previousFrame);
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

    private static void PrintFrameData(ClrRuntime runtime, ClrStackFrame frame, ClrStackFrame? previousFrame)
    {
        if (previousFrame == null)
        {
            Console.WriteLine("    <frame data unavailable for final frame>");
            return;
        }

        Console.WriteLine();
        Console.WriteLine("    Frame data: {0:X} {1:X}", frame.StackPointer, previousFrame.StackPointer);
        var locals = new List<FrameDataEntry>();
        foreach (var ptr in runtime.EnumeratePointersInRange(frame.StackPointer, previousFrame.StackPointer))
        {
            if (!runtime.DataTarget.DataReader.ReadPointer(ptr, out var value))
                continue;

            var objectType = runtime.Heap.GetObjectType(value);
            if (objectType != null)
            {
                locals.Add(new FrameDataEntry
                {
                    Address = value,
                    MethodTable = objectType.MethodTable,
                    Type = objectType.Name
                });
            }
            else
            {
                locals.Add(new FrameDataEntry { Address = value });
            }
        }

        var distinctLocals = locals.Distinct().ToList();
        if (distinctLocals.Count == 0)
        {
            Console.WriteLine("    <no stack parameters or variables found>");
            return;
        }

        foreach (var local in distinctLocals)
        {
            Console.WriteLine("    {0:X} {1:X} {2}", local.MethodTable, local.Address, local.Type);
        }
    }

    private readonly struct FrameDataEntry : IEquatable<FrameDataEntry>
    {
        public ulong MethodTable { get; init; }
        public ulong Address { get; init; }
        public string? Type { get; init; }

        public bool Equals(FrameDataEntry other) => Address == other.Address;

        public override bool Equals(object? obj) =>
            obj is FrameDataEntry other && Equals(other);

        public override int GetHashCode() => Address.GetHashCode();
    }
}
