using System.Text;
using DbgEngExtension;
using Microsoft.Diagnostics.Runtime;

namespace ndbgext;

public class BlockingInfoCommand : DbgEngCommand
{
    private readonly BlockingInfoProvider _blockingInfo;

    public BlockingInfoCommand(BlockingInfoProvider blockingInfo, nint pUnknown, bool redirectConsoleOutput = true)
        : base(pUnknown, redirectConsoleOutput)
    {
        _blockingInfo = blockingInfo;
    }
    
    internal void Run(string args)
    {
        foreach (var runtime in Runtimes)
        {
            var runningThreadInfos = _blockingInfo.GetThreadLockItems(runtime)
                .Where(rti => rti.BlockingDetails != null);
            var byFrames = runningThreadInfos.GroupBy(ti => new
            {
                Frame = ti.BlockingDetails.Frame.ToString(),
                LockingFrame = ti.BlockingDetails.LockingFrame.ToString(),
                TypeName = ti.BlockingDetails.TypeName
            });

            foreach (var byFrame in byFrames)
            {
                Console.WriteLine("Frame: {0}", byFrame.Key.Frame);
                Console.WriteLine("Locking Frame: {0}", byFrame.Key.LockingFrame);
                Console.WriteLine("Type Name: {0}", byFrame.Key.TypeName);
                var threadIs = byFrame.Select(f => $"0x{f.OSThreadId:X}");
                Console.WriteLine("Threads: {0}", string.Join(' ', threadIs));
                Console.WriteLine("Number of threads: {0}", byFrame.Count());
                Console.WriteLine();
            }
        }
    }
}

public class BlockingInfoProvider
{
    private ClrType _rwType;
    private ClrType _rwsType;
    private HashSet<string> _eventTypes;
    
    public BlockingInfoProvider()
    {
        _eventTypes = new HashSet<string>();
        _eventTypes.Add("System.Threading.Mutex");
        _eventTypes.Add("System.Threading.Semaphore");
        _eventTypes.Add("System.Threading.ManualResetEvent");
        _eventTypes.Add("System.Threading.AutoResetEvent");
        _eventTypes.Add("System.Threading.WaitHandle");
        _eventTypes.Add("Microsoft.Win32.SafeHandles.SafeWaitHandle");
    }

    public IReadOnlyList<RunningThreadInfo> GetThreadLockItems(ClrRuntime runtime)
    {
        var result = new List<RunningThreadInfo>();
        
        foreach (var thread in runtime.Threads)
        {
            StringBuilder sb = new StringBuilder();
            int currentFrame = 0;
            ClrStackFrame lastFrame = null;
            BlockingInfo bi = null;
            RunningThreadInfo rti = new RunningThreadInfo();
            rti.OSThreadId = thread.OSThreadId;
            foreach (var frame in thread.EnumerateStackTrace())
            {
                //if (currentFrame > MAX_FRAME_COUNT)
                //{
                    // it is time to check if we got enough information about Task/WorkItem
                    //break;
                //}

                // figure out if there is any lock on the first frame
                // based on lockingInspection.cs | SetThreadWaiters()
                var method = frame.Method;
                if (method == null)
                    continue;
                var type = method.Type;
                if (type == null)
                    continue;

                if (bi == null)
                    switch (method.Name)
                    {
                        case "AcquireWriterLockInternal":
                        case "FCallUpgradeToWriterLock":
                        case "UpgradeToWriterLock":
                        case "AcquireReaderLockInternal":
                        case "AcquireReaderLock":
                            if (type.Name == "System.Threading.ReaderWriterLock")
                            {
                                bi = new BlockingInfo()
                                {
                                    Frame = frame,
                                    TypeName = "ReaderWriterLock"
                                };
                                bi.ObjRef = FindLockObject(
                                    thread.StackLimit,
                                    frame.StackPointer,
                                    IsReaderWriterLock,
                                    runtime
                                    );
                                if (bi.ObjRef == 0)
                                {
                                    bi.ObjRef = FindLockObject(
                                        frame.StackPointer,
                                        thread.StackBase,
                                        IsReaderWriterLock,
                                        runtime
                                        );
                                }
                            }
                            break;

                        case "TryEnterReadLockCore":
                        case "TryEnterReadLock":
                        case "TryEnterUpgradeableReadLock":
                        case "TryEnterUpgradeableReadLockCore":
                        case "TryEnterWriteLock":
                        case "TryEnterWriteLockCore":
                            if (type.Name == "System.Threading.ReaderWriterLockSlim")
                            {
                                bi = new BlockingInfo()
                                {
                                    Frame = frame,
                                    TypeName = "ReaderWriterLockSlim"
                                };
                                bi.ObjRef = FindLockObject(
                                    thread.StackLimit,
                                    frame.StackPointer,
                                    IsReaderWriterSlim,
                                    runtime
                                    );
                                if (bi.ObjRef == 0)
                                {
                                    bi.ObjRef = FindLockObject(
                                        frame.StackPointer,
                                        thread.StackBase,
                                        IsReaderWriterSlim,
                                        runtime
                                        );
                                }
                            }
                            break;

                        case "JoinInternal":
                        case "Join":
                            if (type.Name == "System.Threading.Thread")
                            {
                                bi = new BlockingInfo()
                                {
                                    Frame = frame,
                                    TypeName = "Thread"
                                };

                                // TODO: look for the thread
                            }
                            break;

                        case "Wait":
                        case "ObjWait":
                            if (type.Name == "System.Threading.Monitor")
                            {
                                bi = new BlockingInfo()
                                {
                                    Frame = frame,
                                    TypeName = "Monitor"
                                };

                                // TODO: look for the lock
                            }
                            break;

                        case "WaitAny":
                        case "WaitAll":
                            if (type.Name == "System.Threading.WaitHandle")
                            {
                                bi = new BlockingInfo()
                                {
                                    Frame = frame,
                                    TypeName = "WaitHandle"
                                };

                                bi.ObjRef = FindWaitObjects(
                                    thread.StackLimit,
                                    frame.StackPointer,
                                    "System.Threading.WaitHandle[]",
                                    runtime
                                    );
                                if (bi.ObjRef == 0)
                                    bi.ObjRef = FindWaitObjects(
                                        frame.StackPointer,
                                        thread.StackBase,
                                        "System.Threading.WaitHandle[]",
                                        runtime
                                        );
                            }
                            break;

                        case "WaitOne":
                        case "InternalWaitOne":
                        case "WaitOneNative":
                            if (type.Name == "System.Threading.WaitHandle")
                            {
                                bi = new BlockingInfo()
                                {
                                    Frame = frame,
                                    TypeName = "WaitHandle"
                                };

                                if (_eventTypes == null)
                                {
                                    _eventTypes = new HashSet<string>();
                                    _eventTypes.Add("System.Threading.Mutex");
                                    _eventTypes.Add("System.Threading.Semaphore");
                                    _eventTypes.Add("System.Threading.ManualResetEvent");
                                    _eventTypes.Add("System.Threading.AutoResetEvent");
                                    _eventTypes.Add("System.Threading.WaitHandle");
                                    _eventTypes.Add("Microsoft.Win32.SafeHandles.SafeWaitHandle");
                                }

                                bi.ObjRef = FindWaitHandle(
                                    thread.StackLimit,
                                    frame.StackPointer,
                                    _eventTypes,
                                    runtime
                                    );
                                if (bi.ObjRef == 0)
                                    bi.ObjRef = FindWaitHandle(
                                        frame.StackPointer,
                                        thread.StackBase,
                                        _eventTypes,
                                        runtime
                                        );
                            }
                            break;


                        case "TryEnter":
                        case "ReliableEnterTimeout":
                        case "TryEnterTimeout":
                        case "Enter":
                            if (type.Name == "System.Threading.Monitor")
                            {
                                bi = new BlockingInfo()
                                {
                                    Frame = frame,
                                    TypeName = "Monitor"
                                };

                                // NOTE: this method is not implemented yet
                                bi.ObjRef = FindMonitor(
                                    thread.StackLimit,
                                    frame.StackPointer
                                    );
                            }
                            break;

                        default:
                            break;
                    }
                else // keep track of the frame BEFORE locking
                {
                    if ((bi.LockingFrame == null) && (!frame.Method.Type.Name.Contains("System.Threading")))
                    {
                        bi.LockingFrame = frame;
                    }
                }

                // look for task/work item details
                if (frame.Kind != ClrStackFrameKind.ManagedMethod)
                {
                    continue;
                }

                if (frame.Method.Type.Name == "System.Threading.Tasks.Task")
                {
                    if (frame.Method.Name == "Execute")
                    {
                        // the previous frame should contain the name of the method called by the task
                        if (lastFrame != null)
                        {
                            rti.RootType = ThreadRoot.Task;
                            rti.RootMethod = lastFrame.FrameName;
                        }

                        break;
                    }
                }
                else
                if (frame.Method.Type.Name == "System.Threading.ExecutionContext")
                {
                    if (frame.Method.Name == "RunInternal")
                    {
                        // the previous frame should contain the name of the method called by QueueUserWorkItem
                        if (lastFrame != null)
                        {
                            rti.RootType = ThreadRoot.WorkItem;
                            rti.RootMethod = lastFrame.FrameName;
                        }

                        break;
                    }
                }
                else
                {
                    lastFrame = frame;
                }

                currentFrame++;
            }
            rti.BlockingDetails = bi;

            result.Add(rti);
        }

        return result;
    }
    private ulong FindMonitor(ulong start, ulong stop)
    {
        // This code from lockinspection requires too much internal code  :^(
        //
        //ulong obj = 0;
        //foreach (ulong ptr in EnumeratePointersInRange(start, stop))
        //{
        //    ulong tmp = 0;
        //    if (_clr.ReadPointer(ptr, out tmp))
        //    {
        //        if (_syncblks.TryGetValue(tmp, out tmp))
        //        {
        //            return tmp;
        //        }
        //    }
        //}

        return 0;
    }
    
    private ulong FindLockObject(ulong start, ulong stop, Func<ulong, ClrType, bool> isCorrectType, ClrRuntime clr)
    {
        foreach (ulong ptr in EnumeratePointersInRange(start, stop, clr))
        {
            ulong val = 0;
            if (clr.DataTarget.DataReader.ReadPointer(ptr, out val))
            {
                if (isCorrectType(val, clr.Heap.GetObjectType(val)))
                    return val;
            }
        }

        return 0;
    }

    private ulong FindWaitHandle(ulong start, ulong stop, HashSet<string> eventTypes, ClrRuntime clr)
    {
        foreach (ulong obj in EnumerateObjectsOfTypes(start, stop, eventTypes, clr))
            return obj;

        return 0;
    }

    private ulong FindWaitObjects(ulong start, ulong stop, string typeName, ClrRuntime clr)
    {
        foreach (ulong obj in EnumerateObjectsOfType(start, stop, typeName, clr))
            return obj;

        return 0;
    }
    
    private IEnumerable<ulong> EnumerateObjectsOfTypes(ulong start, ulong stop, HashSet<string> types, ClrRuntime clr)
    {
        foreach (ulong ptr in EnumeratePointersInRange(start, stop, clr))
        {
            ulong obj;
            if (clr.DataTarget.DataReader.ReadPointer(ptr, out obj))
            {
                //if (clr.Heap.IsInHeap(obj))
                //{
                    ClrType type = null;

                    try
                    {
                        type = clr.Heap.GetObjectType(obj);
                    }
                    catch (Exception)
                    {
                        // it happens sometimes   :^(
                    }

                    int sanity = 0;
                    while (type != null)
                    {
                        if (types.Contains(type.Name))
                        {
                            yield return obj;
                            break;
                        }

                        type = type.BaseType;

                        if (sanity++ == 16)
                            break;
                    }
                //}
            }
        }
    }
    private IEnumerable<ulong> EnumerateObjectsOfType(ulong start, ulong stop, string typeName, ClrRuntime clr)
    {
        foreach (ulong ptr in EnumeratePointersInRange(start, stop, clr))
        {
            ulong obj;
            if (clr.DataTarget.DataReader.ReadPointer(ptr, out obj))
            {
                //if (clr.Heap.IsInHeap(obj))
                //{
                    ClrType type = clr.Heap.GetObjectType(obj);
                    if (type != null)
                    {
                        int sanity = 0;
                        while (type != null)
                        {
                            if (type.Name == typeName)
                            {
                                yield return obj;
                                break;
                            }

                            type = type.BaseType;

                            if (sanity++ == 16)
                                break;
                        }
                    }
                //}
            }
        }
    }
        
    private bool IsReaderWriterLock(ulong obj, ClrType type)
    {
        if (type == null)
            return false;

        if (_rwType == null)
        {
            if (type.Name != "System.Threading.ReaderWriterLock")
                return false;

            _rwType = type;
            return true;
        }

        return _rwType == type;
    }

    private bool IsReaderWriterSlim(ulong obj, ClrType type)
    {
        if (type == null)
            return false;

        if (_rwsType == null)
        {
            if (type.Name != "System.Threading.ReaderWriterLockSlim")
                return false;

            _rwsType = type;
            return true;
        }

        return _rwsType == type;
    }
    
    private IEnumerable<ulong> EnumeratePointersInRange(ulong start, ulong stop, ClrRuntime clr)
    {
        uint diff = (uint)clr.DataTarget.DataReader.PointerSize;

        if (start > stop)
            for (ulong ptr = stop; ptr <= start; ptr += diff)
                yield return ptr;
        else
            for (ulong ptr = stop; ptr >= start; ptr -= diff)
                yield return ptr;
    }
}
public class BlockingInfo
{
    public ClrStackFrame Frame { get; set; }
    public ClrStackFrame LockingFrame { get; internal set; }
    public ulong ObjRef { get; set; }
    public string TypeName { get; set; }
}

public class RunningThreadInfo
{
    public ulong OSThreadId { get; set; }
    public ThreadRoot RootType { get; set; }
    public string RootMethod { get; set; }
    public BlockingInfo BlockingDetails { get; set; }
}