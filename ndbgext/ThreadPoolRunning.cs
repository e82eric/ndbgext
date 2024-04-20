using DbgEngExtension;
using Microsoft.Diagnostics.Runtime;

namespace ndbgext;

public class ThreadPoolRunningCommand : DbgEngCommand
{
    public ThreadPoolRunningCommand(nint pUnknown, ThreadPool threadPool, bool redirectConsoleOutput = true)
        : base(pUnknown, redirectConsoleOutput)
    {
    }
}

public class TreadPoolRunning
{
}