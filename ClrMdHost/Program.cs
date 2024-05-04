using Microsoft.Diagnostics.Runtime;

namespace ClrMdHost;

class Program
{
    static void Main(string[] args)
    {
        string dumpFilePath = @"C:\a\csdecompile.exe_240121_174819.dmp";

        using (DataTarget dataTarget = DataTarget.LoadDump(dumpFilePath))
        {
            var runtime = dataTarget.ClrVersions.Single().CreateRuntime();
            var clrThread = runtime.Threads.Single(t => t.OSThreadId == 0x1e80);
            foreach (var frame in clrThread.EnumerateStackTrace())
            {
                var method = frame.Method?.Name;
                var ip = frame.InstructionPointer;
                var ilOffset = frame.Method?.GetILOffset(frame.InstructionPointer);
                
                var ilOffsets = frame.Method?.ILOffsetMap.Where(m =>
                m.StartAddress <= ip && m.EndAddress >= ip);
                if (ilOffsets != null && ilOffsets.Count() > 0)
                {
                    //var last = ilOffsets?.Last(l => l.ILOffset > 0);
                    //Console.WriteLine("Last {0} {1:X} {2:X}", last?.ILOffset, last?.StartAddress, last?.EndAddress);
                }
                if (ilOffsets != null)
                {
                    foreach (var il in ilOffsets)
                    {
                        Console.WriteLine("{0} {1:X} {2:X} {3}", il.ILOffset, il.StartAddress, il.EndAddress, il.EndAddress - il.StartAddress);
                    }
                }

                Console.WriteLine("{0} {1:X} {2}", method, ip, ilOffset);
            }
        }
    }
}