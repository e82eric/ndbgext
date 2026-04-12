using System.Collections.Immutable;
using Microsoft.Diagnostics.ExtensionCommands;
using Microsoft.Diagnostics.Runtime;

namespace ClrMdHost;

class Program
{
    static void Main(string[] args)
    {
        
        //using (var dataTarget = DataTarget.AttachToProcess(26620, true))
        //{
        //}


        //string dumpFilePath = @"C:\a\csdecompile.exe_240121_174819.dmp";
        // string dumpFilePath = @"C:\a\FrameLocals.exe_net8_2.dmp";
        //
        // using (DataTarget dataTarget = DataTarget.LoadDump(dumpFilePath))
        // {
        //     var clrRuntime = dataTarget.ClrVersions.Single().CreateRuntime();
        //     var gcRootCommand = new GCRootCommand(
        //         new RootCacheService(clrRuntime),
        //         new StaticVariableService(clrRuntime),
        //         clrRuntime);
        //     
        //     //gcRootCommand.TargetAddress = "0000020b7f014870";
        //     //gcRootCommand.NoStacks = false;
        //     ulong methodTable = 0x00007ff831f0ed60;
        //     //ulong methodTable = 0x00007ffbe28206f8;
        //     gcRootCommand.InvokeMethodTable(methodTable);
        //}
    }
    
    private static ulong GetDistance(ILToNativeMap entry, ulong nativeOffset)
    {
        ulong distance = 0;
        if (nativeOffset < entry.StartAddress)
        {
            distance = entry.StartAddress - nativeOffset;
        }
        else if (nativeOffset > entry.EndAddress)
        {
            distance = nativeOffset - entry.EndAddress;
        }

        return distance;
    }
    
    private static int GetILOffsetForNativeOffset(ClrMethod method, ulong ip)
    {
        ImmutableArray<ILToNativeMap> ilmap = method.ILOffsetMap;
        if (ilmap.IsDefaultOrEmpty)
        {
            return -1;
        }

        (ulong Distance, int Offset) closest = (ulong.MaxValue, -1);
        foreach (ILToNativeMap entry in ilmap)
        {
            ulong distance = GetDistance(entry, ip);
            if (distance == 0)
            {
                return entry.ILOffset;
            }

            if (distance < closest.Distance)
            {
                closest = (distance, entry.ILOffset);
            }
        }

        return closest.Offset;
    }   
}
