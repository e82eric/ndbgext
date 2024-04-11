using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ndbgext;

public static unsafe class Extension
{
    [UnmanagedCallersOnly(EntryPoint = "DebugExtensionInitialize")]
    public static unsafe int DebugExtensionInitialize(uint* version, uint* flags)
    {
        *version = (1 & 0xffff) << 16;
        *flags = 0;
        return 0;
    }

    [UnmanagedCallersOnly(EntryPoint = "clruniqstack", CallConvs = new[] { typeof(CallConvStdcall) })]
    public static int UniqClrStack(nint pUnknown, nint args)
    {
        try
        {
            ClrUniqStack cmd = new(pUnknown);
            string? arguments = Marshal.PtrToStringAnsi(args);
            cmd.Run(arguments ?? "");
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"Failed to run {nameof(UniqClrStack)} command.");
            Console.Error.WriteLine(e);
        }

        return 0;
    }

    [UnmanagedCallersOnly(EntryPoint = "threads", CallConvs = new[] { typeof(CallConvStdcall) })]
    public static int Threads(nint pUnknown, nint args)
    {
        try
        {
            Threads cmd = new(pUnknown);
            string? arguments = Marshal.PtrToStringAnsi(args);
            cmd.Run(arguments ?? "");
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"Failed to run {nameof(Threads)} command.");
            Console.Error.WriteLine(e);
        }

        return 0;
    }
}
