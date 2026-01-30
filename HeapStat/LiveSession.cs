using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Session;
using System.Diagnostics;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;

namespace LiveStacks
{
    class LiveSession
    {
        private readonly KernelTraceEventParser.Keywords _kernelKeyword = KernelTraceEventParser.Keywords.Profile;
        private TraceEventSession _session;
        private readonly int _processId;

        public PidStacks Stacks { get; }

        public LiveSession(int processID)
        {
            _processId = processID;
            Stacks = new PidStacks();
        }

        /// <summary>
        /// Starts the ETW session, and processes the stack samples that come in. This method
        /// does not return until another thread calls <see cref="Stop"/> to stop the session.
        /// While this method is executing, live stack aggregates can be obtained from the 
        /// <see cref="Stacks"/> property, which is thread-safe.
        /// </summary>
        public void Start()
        {
            _session = new TraceEventSession($"LiveStacks-{Process.GetCurrentProcess().Id}");

            // TODO Make the CPU sampling interval configurable, although changing it doesn't seem to work in a VM?
            // _session.CpuSampleIntervalMSec = 10.0f;

            // TODO Should we use _session.StackCompression? What would the events look like?

            _session.EnableKernelProvider(_kernelKeyword, stackCapture: _kernelKeyword);
            _session.Source.Kernel.StackWalkStack += OnKernelStackEvent;

            _session.Source.Process();
        }
        
        private void OnKernelStackEvent(StackWalkStackTraceData stack)
        {
            if (stack.ProcessID != _processId)
                return;

            ulong[] addresses = new ulong[stack.FrameCount];
            int recordedIdx = 0;
            for (int originalFrameIdx = 0; originalFrameIdx < stack.FrameCount; ++originalFrameIdx)
            {
                ulong ip = stack.InstructionPointer(originalFrameIdx);
                // drop kernel frames
                if (ip < 0x8000000000000000)
                    addresses[recordedIdx++] = ip;
            }
            if (recordedIdx > 0) // This could have been a purely kernel stack
            {
                Stacks.AddStack(addresses);
            }
        }

        public void Stop()
        {
            _session.Stop();
        }
    }
}
