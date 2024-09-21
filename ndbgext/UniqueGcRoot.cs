using System.Collections.ObjectModel;
using DbgEngExtension;
using ICSharpCode.Decompiler.Util;
using Microsoft.Diagnostics.Runtime;

namespace ndbgext;

public class UniqGcRootCommand : DbgEngCommand
{
    public UniqGcRootCommand(nint pUnknown, bool redirectConsoleOutput = true)
        : base(pUnknown, redirectConsoleOutput)
    {
    }
    
    internal void Run(string args)
    {
        var runtime = Runtimes.First();
        var command = new GCRootCommand(
            new RootCacheService(runtime),
            new StaticVariableService(runtime),
            runtime);
        var argsSplit = args.Split(' ');

        var methodTableProvided = false;
        ulong methodTable = default;
        int max = 25;
        
        for (int i = 0; i < argsSplit.Length; i++)
        {
            var key = argsSplit[i];
            if (i + 1 >= argsSplit.Length)
            {
                Console.WriteLine("Value for {0} parameter was not provided", key);
                return;
            }

            switch (key)
            {
                case "-mt":
                    if (Helper.TryParseAddress(argsSplit[i + 1], out methodTable))
                    {
                        methodTableProvided = true;
                    }
                    else
                    {
                        Console.WriteLine("Unable to parse method table: {0}",argsSplit[i + 1]);
                        return;
                    }

                    i++;
                    break;
                case "-max":
                    if (!int.TryParse(argsSplit[i + 1], out max))
                    {
                        Console.WriteLine("Unable to parse max: {0}", argsSplit[i + 1]);
                        return;
                    }

                    i++;
                    break;
            }
        }

        if (!methodTableProvided)
        {
            Console.WriteLine("MethodTable not provided");
            return;
        }
        
        command.InvokeMethodTable(methodTable, max);    
    }
}

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

    public void InvokeMethodTable(ulong methodTable, int max)
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

            if (ctr > max)
            {
                break;
            }
        }
                
        result.SortBy(r => r.count);
        foreach (var path in result)
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

    private void PrintAllRoots(GCRoot gcroot, int limit, List<(List<(ulong, string)> path, int count)> paths)
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

public class RootCacheService
{
    private List<(ulong Source, ulong Target)> _dependentHandles;
    private ReadOnlyCollection<ClrRoot> _handleRoots;
    private ReadOnlyCollection<ClrRoot> _finalizerRoots;
    private ReadOnlyCollection<ClrRoot> _stackRoots;
    private bool _printedWarning;
    private bool _printedStackWarning;

    public RootCacheService(ClrRuntime runtime)
    {
        Runtime = runtime;
    }
        
    //[ServiceImport]
    //public IConsoleService Console { get; set; }

    //[ServiceImport]
    public ClrRuntime Runtime { get; set; }

    public ReadOnlyCollection<(ulong Source, ulong Target)> GetDependentHandles()
    {
        InitializeHandleRoots();

        // We keep _dependentHandles as a List instead of ReadOnlyCollection so we can use
        // List<>.BinarySearch.
        return _dependentHandles.AsReadOnly();
    }

    public bool IsDependentHandleLink(ulong source, ulong target)
    {
        int i = _dependentHandles.BinarySearch((source, target));
        return i >= 0;
    }

    public IEnumerable<ClrRoot> EnumerateRoots(bool includeFinalizer = true)
    {
        PrintWarning();

        foreach (ClrRoot root in GetHandleRoots())
        {
            //Console.CancellationToken.ThrowIfCancellationRequested();
            yield return root;
        }

        if (includeFinalizer)
        {
            foreach (ClrRoot root in GetFinalizerQueueRoots())
            {
                //Console.CancellationToken.ThrowIfCancellationRequested();
                yield return root;
            }
        }

        // If we made it here without the user breaking out of the enumeration
        // then we've already printed a warning on this command run, we don't
        // need to also print the stack warning.
        _printedStackWarning = true;
        foreach (ClrRoot root in GetStackRoots())
        {
            //Console.CancellationToken.ThrowIfCancellationRequested();
            yield return root;
        }
    }

    public ReadOnlyCollection<ClrRoot> GetHandleRoots()
    {
        InitializeHandleRoots();
        return _handleRoots;
    }


    private void InitializeHandleRoots()
    {
        if (_handleRoots is not null && _dependentHandles is not null)
        {
            return;
        }

        PrintWarning();
        List<(ulong Source, ulong Target)> dependentHandles = new();
        List<ClrRoot> handleRoots = new();

        foreach (ClrHandle handle in Runtime.EnumerateHandles())
        {
            //Console.CancellationToken.ThrowIfCancellationRequested();

            if (handle.HandleKind == ClrHandleKind.Dependent)
            {
                dependentHandles.Add((handle.Object, handle.Dependent));
            }

            if (!handle.IsStrong)
            {
                continue;
            }

            handleRoots.Add(handle);
        }

        // Sort dependentHandles so it can be binary searched
        dependentHandles.Sort();

        _handleRoots = handleRoots.AsReadOnly();
        _dependentHandles = dependentHandles;
    }

    private ReadOnlyCollection<ClrRoot> GetFinalizerQueueRoots()
    {
        if (_finalizerRoots is not null)
        {
            return _finalizerRoots;
        }

        PrintWarning();

        // This should be fast, there's rarely many FQ roots
        _finalizerRoots = Runtime.Heap.EnumerateFinalizerRoots().ToList().AsReadOnly();
        return _finalizerRoots;
    }

    private void PrintWarning()
    {
        if (!_printedWarning)
        {
            //Console.WriteLineWarning("Caching GC roots, this may take a while.");
            //Console.WriteLineWarning("Subsequent runs of this command will be faster.");
            Console.WriteLine();
            _printedWarning = true;
        }
    }

    private ReadOnlyCollection<ClrRoot> GetStackRoots()
    {
        if (_stackRoots is not null)
        {
            return _stackRoots;
        }

        // Stack roots can take an extra long time to walk, and one mode of !gcroot skips enumerating stack roots.  If the user
        // calls "!gcroot -nostack" they will get a warning the first time, but if they call it again without "-nostack" they
        // may be surprised by a very long pause.  We skip this second message if the user is calling EnumerateRoots().
        if (!_printedStackWarning)
        {
            //Console.WriteLineWarning("Caching GC stack roots, this may take a while.");
            //Console.WriteLineWarning("Subsequent runs of this command will be faster.");
            Console.WriteLine();
            _printedStackWarning = true;
        }

        List<ClrRoot> stackRoots = new();
        foreach (ClrThread thread in Runtime.Threads.Where(thread => thread.IsAlive))
        {
            //Console.CancellationToken.ThrowIfCancellationRequested();

            foreach (ClrRoot root in thread.EnumerateStackRoots())
            {
                //Console.CancellationToken.ThrowIfCancellationRequested();
                stackRoots.Add(root);
            }
        }

        _stackRoots = stackRoots.AsReadOnly();
        return _stackRoots;
    }
}


public class StaticVariableService
{
    public StaticVariableService(ClrRuntime runtime)
    {
        Runtime = runtime;
    }
    
    private Dictionary<ulong, ClrStaticField> _fields;
    private IEnumerator<(ulong Address, ClrStaticField Static)> _enumerator;

    private ClrRuntime Runtime { get; set; }

    /// <summary>
    /// Returns the static field at the given address.
    /// </summary>
    /// <param name="address">The address of the static field.  Note that this is not a pointer to
    /// an object, but rather a pointer to where the CLR runtime tracks the static variable's
    /// location.  In all versions of the runtime, address will live in the middle of a pinned
    /// object[].</param>
    /// <param name="field">The field corresponding to the given address.  Non-null if return
    /// is true.</param>
    /// <returns>True if the address corresponded to a static variable, false otherwise.</returns>
    public bool TryGetStaticByAddress(ulong address, out ClrStaticField field)
    {
        if (_fields is null)
        {
            _fields = new();
            _enumerator = EnumerateStatics().GetEnumerator();
        }

        if (_fields.TryGetValue(address, out field))
        {
            return true;
        }

        // pay for play lookup
        if (_enumerator is not null)
        {
            do
            {
                _fields[_enumerator.Current.Address] = _enumerator.Current.Static;
                if (_enumerator.Current.Address == address)
                {
                    field = _enumerator.Current.Static;
                    return true;
                }
            } while (_enumerator.MoveNext());

            _enumerator = null;
        }

        return false;
    }

    private IEnumerable<(ulong Address, ClrStaticField Static)> EnumerateStatics()
    {
        ClrAppDomain shared = Runtime.SharedDomain;

        foreach (ClrModule module in Runtime.EnumerateModules())
        {
            foreach ((ulong mt, _) in module.EnumerateTypeDefToMethodTableMap())
            {
                ClrType type = Runtime.GetTypeByMethodTable(mt);
                if (type is null)
                {
                    continue;
                }

                foreach (ClrStaticField stat in type.StaticFields)
                {
                    foreach (ClrAppDomain domain in Runtime.AppDomains)
                    {
                        ulong address = stat.GetAddress(domain);
                        if (address != 0)
                        {
                            yield return (address, stat);
                        }
                    }

                    if (shared is not null)
                    {
                        ulong address = stat.GetAddress(shared);
                        if (address != 0)
                        {
                            yield return (address, stat);
                        }
                    }
                }
            }
        }
    }
}
