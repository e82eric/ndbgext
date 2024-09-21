// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Diagnostics.Runtime;

namespace Microsoft.Diagnostics.ExtensionCommands
{
    public class GCRootCommand
    {
        public GCRootCommand(
            RootCacheService rootCacheService,
            StaticVariableService? staticVariables,
            ClrRuntime? runtime)
        {
            RootCache = rootCacheService;
            StaticVariables = staticVariables;
            Runtime = runtime;
        }
        
        private ClrRoot _lastRoot;
        private RootCacheService RootCache { get; set; }
        private StaticVariableService StaticVariables { get; set; }
        private ClrRuntime Runtime { get; set; }
        public int? Limit { get; set; }

        public void InvokeMethodTable(ulong methodTable)
        {
            List<(List<(ulong, string)> path, int count)> result = new();

            int ctr = 0;
            foreach (var clrObject in Runtime.Heap.EnumerateObjects())
            {
                if (clrObject.Type?.MethodTable == methodTable)
                {
                    Invoke(result, clrObject.Address);
                    ctr++;
                    if(ctr % 10 == 0)
                    {
                        Console.WriteLine("Checked {0} objects", ctr);
                    }
                }
            }

            var sorted = result.OrderByDescending(r => r.count);
            foreach (var path in sorted)
            {
                foreach (var type in path.path)
                {
                    Console.WriteLine("{0:x} {1}", type.Item1, type.Item2);
                }
                Console.WriteLine("Number of objects: {0}", path.count);
                Console.WriteLine();
            }
        }

        private void Invoke(List<(List<(ulong, string)> path, int count)> paths, ulong address)
        {
            GCRoot gcroot = new(Runtime.Heap, (found) =>
            {
                return found == address;
            });

            int limit = Limit ?? int.MaxValue;

            PrintAllRoots(gcroot, limit, paths);
        }

        private int PrintAllRoots(GCRoot gcroot, int limit, List<(List<(ulong, string)> path, int count)> paths)
        {
            var updatedIndexes = new List<int>();
            int count = 0;
            foreach (ClrRoot root in RootCache.EnumerateRoots())
            {
                if (count >= limit)
                {
                    break;
                }

                GCRoot.ChainLink item = gcroot.FindPathFrom(root.Object);
                if (item is not null)
                {
                    var updatedIndex = PrintPath(root, item, paths);
                    if (!updatedIndexes.Contains(updatedIndex))
                    {
                        updatedIndexes.Add(updatedIndex);
                    }
                    count++;
                }
            }

            foreach (var updatedIndex in updatedIndexes)
            {
                var path = paths[updatedIndex];
                path.count = path.count += 1;
                paths[updatedIndex] = path;
            }

            return count;
        }

        private int PrintPath(ClrRoot root, GCRoot.ChainLink link, List<(List<(ulong methodTable, string name)> path, int count)> paths)
        {
            var updatedIndex = PrintPath(RootCache, StaticVariables, Runtime.Heap, link, paths);
            return updatedIndex;
        }

        private static int PrintPath(RootCacheService rootCache, StaticVariableService statics,
            ClrHeap heap, GCRoot.ChainLink link, List<(List<(ulong methodTable, string name)> path, int count)> paths)
        {
            List<(ulong methodTable, string name)> localPaths = new();

            bool first = true;
            bool isPossibleStatic = true;

            ClrObject firstObj = default;

            ulong prevObj = 0;
            while (link != null)
            {
                ClrObject obj = heap.GetObject(link.Object);

                // Check whether this link is a dependent handle
                string extraText = "";
                bool isDependentHandleLink = rootCache.IsDependentHandleLink(prevObj, link.Object);
                if (isDependentHandleLink)
                {
                    extraText = "(dependent handle)";
                }

                // Print static variable info.  In all versions of the runtime, static variables are stored in
                // a pinned object array.  We check if the first link in the chain is an object[], and if so we
                // check if the second object's address is the location of a static variable.  We could further
                // narrow this by checking the root type, but that needlessly complicates this code...we can't
                // get false positives or negatives here (as nothing points to static variable object[] other
                // than the root).
                if (first)
                {
                    firstObj = obj;
                    isPossibleStatic = firstObj.IsValid && firstObj.IsArray && firstObj.Type.Name == "System.Object[]";
                    first = false;
                }
                else if (isPossibleStatic)
                {
                    if (statics is not null && !isDependentHandleLink)
                    {
                        foreach (ClrReference reference in firstObj.EnumerateReferencesWithFields(carefully: false,
                                     considerDependantHandles: false))
                        {
                            if (reference.Object == obj)
                            {
                                ulong address = firstObj + (uint)reference.Offset;

                                if (statics.TryGetStaticByAddress(address, out ClrStaticField field))
                                {
                                    extraText = $"(static variable: {field.Type?.Name ?? "Unknown"}.{field.Name})";
                                    break;
                                }
                            }
                        }
                    }

                    // only the first object[] in the chain is possible to be the static array
                    isPossibleStatic = false;
                }

                //objectOutput.WriteRow("->", obj, obj.Type, extraText);
                localPaths.Add((obj.Type.MethodTable, obj.Type.Name));

                prevObj = link.Object;
                link = link.Next;
            }

            var hasMatch = false;
            var result = 0;
            for (var index = 0; index < paths.Count; index++)
            {
                var path = paths[index];
                if (path.path.Count == localPaths.Count)
                {
                    var allMatch = true;
                    for (var i = 0; i < path.path.Count; i++)
                    {
                        var type = path.path[i];
                        var localType = localPaths[i];
                        if (type.methodTable != localType.methodTable)
                        {
                            result = index;
                            allMatch = false;
                            break;
                        }
                    }

                    if (allMatch)
                    {
                        hasMatch = true;
                        break;
                    }
                }
            }

            if (!hasMatch)
            {
                //setting to 0 so it can be updated to 1 in caller method
                paths.Add((localPaths, 0));
                result = paths.Count - 1;
            }

            return result;
        }
    }
}
