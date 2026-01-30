using Microsoft.Diagnostics.Runtime;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LiveStacks
{
    struct Symbol : IEquatable<Symbol>
    {
        public string ModuleName { get; set; }
        public string MethodName { get; set; }
        public ulong OffsetInMethod { get; set; }
        public ulong Address { get; set; }

        public bool Equals(Symbol symbol)
        {
            return Address == symbol.Address;
        }

        public override bool Equals(object obj)
        {
            if (!(obj is Symbol))
                return false;

            Symbol other = (Symbol)obj;
            return Equals(other);
        }

        public override int GetHashCode()
        {
            return Address.GetHashCode();
        }

        public static bool operator ==(Symbol a, Symbol b) => a.Equals(b);
        public static bool operator !=(Symbol a, Symbol b) => !a.Equals(b);

        public override string ToString()
        {
            if (String.IsNullOrEmpty(MethodName) && String.IsNullOrEmpty(ModuleName))
                return $"{Address,16:X}";
            else
                return $"{ModuleName}!{MethodName}+0x{OffsetInMethod:X}";
        }
    }

    class ProcessStackResolver
    {
        private readonly ManagedTarget _managedTarget;
        private readonly SymbolCache _symbolCache = new SymbolCache(1024);

        public ProcessStackResolver(int processId)
        {
            _managedTarget = new ManagedTarget(processId);
        }

        public Symbol[] Resolve(ulong[] addresses)
        {
            return addresses.Where(address => address != 0)
                .Select(address => _symbolCache.GetOrAdd(address, Resolve))
                .ToArray();
        }

        private Symbol Resolve(ulong address)
        {
            var result = _managedTarget.ResolveSymbol(address);

            // TODO We are currently not resolving kernel symbols at all. They do not lie in
            //      any user-space module, so we don't recognize the addresses.
            return result;
        }
    }

    class ManagedTarget
    {
        private DataTarget _dataTarget;
        private List<ClrRuntime> _runtimes;
        private List<ModuleInfo> _modules;

        public ManagedTarget(int processID)
        {
            // TODO This can only be done from a process with the same bitness, so we might want to move this out
            _dataTarget = DataTarget.AttachToProcess(processID, false);
            _runtimes = _dataTarget.ClrVersions.Select(clr => clr.CreateRuntime()).ToList();
            _modules = _dataTarget.EnumerateModules().ToList();
        }

        public Symbol ResolveSymbol(ulong address)
        {
            var method = _runtimes.Select(runtime => runtime.GetMethodByInstructionPointer(address))
                .FirstOrDefault(m => m != null);
            if (method != null)
            {
                return new Symbol
                {
                    ModuleName = Path.GetFileName(method.Type?.Module?.Name ?? "[unknown]"),
                    MethodName = method.Signature,
                    OffsetInMethod = (uint)(address - method.NativeCode),
                    Address = address
                };
            }
            var module = _modules.FirstOrDefault(
                m => m.ImageBase <= address && (m.ImageBase + (ulong)m.ImageSize) > address);
            return new Symbol
            {
                ModuleName = module?.FileName ?? "[unknown]",
                OffsetInMethod = (uint)(address - module?.ImageBase ?? 0),
                Address = address
            };
        }
    }
}