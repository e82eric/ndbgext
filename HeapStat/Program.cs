using System;
using System.Collections.Generic;
using Microsoft.Diagnostics.Runtime;

namespace HeapStat
{
    internal static class Program
    {
        public static void RunHeapDump(int pid)
        {
            var gcDumper = new GCHeapDumper(Console.Out);
            gcDumper.SaveETL = true;
            gcDumper.UseETW = true;
            gcDumper.DumpLiveHeap(pid, "c:\\users\\eric\\test.gcdump");
        }
        
        private static int Usage(string error)
        {
            if (!string.IsNullOrEmpty(error))
                Console.Error.WriteLine("Error: " + error);

            Console.Error.WriteLine("Usage:");
            Console.Error.WriteLine("  DumpHeapStatNetFx.exe analyze  <pid>");
            Console.Error.WriteLine("  DumpHeapStatNetFx.exe heapdump <pid>");
            Console.Error.WriteLine("  DumpHeapStatNetFx.exe gcroot   <pid> <type>");
            Console.Error.WriteLine();
            Console.Error.WriteLine("Examples:");
            Console.Error.WriteLine("  DumpHeapStatNetFx.exe analyze  1234");
            Console.Error.WriteLine("  DumpHeapStatNetFx.exe heapdump 1234");
            Console.Error.WriteLine("  DumpHeapStatNetFx.exe gcroot   1234 System.String");
            return 2;
        }

        private static void RunGcRoot(int pid, string gcRootType)
        {
            try
            {
                using (DataTarget target = DataTarget.AttachToProcess(pid, false))
                {
                    ClrRuntime runtime = target.ClrVersions[0].CreateRuntime();

                    if (gcRootType != null)
                    {
                        ClrObject? gcRootObj = null;
                        foreach (var obj in runtime.Heap.EnumerateObjects())
                        {
                            if (obj.Type?.Name == gcRootType)
                            {
                                gcRootObj = obj;
                                break;
                            }
                        }

                        if (gcRootObj != null)
                        {
                            var consoleService = new ConsoleService();
                            var gcRoot = new GCRootCommand(
                                new MemoryServiceFromDataReader(runtime.DataTarget.DataReader),
                                new RootCacheService(runtime, consoleService),
                                new StaticVariableService(runtime),
                                consoleService);

                            gcRoot.TargetAddress = gcRootObj.Value.Address;
                            gcRoot.Invoke(runtime);
                            return;
                        }
                    }

                }
            }
            catch (Exception e)
            {
            }
        }

        private static void RunAnalyze(int pid)
        {
            try
            {
                Console.WriteLine("LiveStacks: (Collecting for 30s Top 10 stacks)");
                LiveStacks.LiveStacks.Run(pid, TimeSpan.FromSeconds(30), 10, 0);
                
                using (DataTarget target = DataTarget.AttachToProcess(pid, false))
                {
                    ClrRuntime runtime = target.ClrVersions[0].CreateRuntime();

                    Console.WriteLine();
                    Console.WriteLine("HeapStat:");
                    HeapStat.Run(runtime);

                    Console.WriteLine();
                    Console.WriteLine("Threads:");
                    SosLikeThreads.Dump(runtime);
                    
                    Console.WriteLine();
                    Console.WriteLine("Unique call stacks");
                    new ClrUniqStack().Run(new []{runtime});
                    
                    Console.WriteLine();
                    Console.WriteLine("Task stacks");
                    new DumpAsyncCommand().Execute(new List<ClrRuntime>(){runtime});
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
            }
        }


        public static int Main(string[] args)
        {
            if (args == null || args.Length == 0)
            {
                return Usage("Missing command.");
            }

            string cmd = args[0].ToLowerInvariant();

            switch (cmd)
            {
                case "analyze":
                    if (args.Length != 2)
                        return Usage("Expected: analyze <pid>");

                    if (!int.TryParse(args[1], out int analyzePid))
                        return Usage("Invalid pid for analyze.");

                    RunAnalyze(analyzePid);
                    return 0;

                case "heapdump":
                    if (args.Length != 2)
                        return Usage("Expected: heapdump <pid>");

                    if (!int.TryParse(args[1], out int heapdumpPid))
                        return Usage("Invalid pid for heapdump.");

                    RunHeapDump(heapdumpPid);
                    return 0;

                case "gcroot":
                    if (args.Length != 3)
                        return Usage("Expected: gcroot <pid> <type>");

                    if (!int.TryParse(args[1], out int gcrootPid))
                        return Usage("Invalid pid for gcroot.");

                    string gcRootType = args[2];
                    if (string.IsNullOrWhiteSpace(gcRootType))
                        return Usage("Missing <type> for gcroot.");

                    RunGcRoot(gcrootPid, gcRootType);
                    return 0;

                case "-h":
                case "--help":
                case "/?":
                    return Usage(null);

                default:
                    return Usage("Unknown command: " + args[0]);
            }
        }
    }
}