using System.Runtime.InteropServices;
using DbgEngExtension;
using Microsoft.Diagnostics.Runtime;

namespace ndbgext;

public class SaveModuleCommand : DbgEngCommand
{
    private readonly SaveModuleProvider _provider;

    public SaveModuleCommand(SaveModuleProvider provider, nint pUnknown, bool redirectConsoleOutput = true)
        : base(pUnknown, redirectConsoleOutput)
    {
        _provider = provider;
    }

    internal void Run(string args)
    {
        var arguments = args.Split(' ');
        if (arguments.Length != 1)
        {
            Console.WriteLine("Usage !til.savemodule modulename");
        }
        else
        {
            var moduleName = arguments[0];
            var directory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var directoryInfo = new DirectoryInfo(directory);
            foreach (var runtime in Runtimes)
            {
                _provider.Run(runtime, moduleName, directoryInfo);
            }
        }
    }
}

public class SaveModuleProvider
{
    private const ushort ExpectedDosHeaderMagic = 0x5A4D;   // MZ
    
    public void Run(ClrRuntime runtime, string moduleName, DirectoryInfo directory)
    {
        var dataTarget = runtime.DataTarget;
        var modules = dataTarget.DataReader.EnumerateModules()
            .Where(m => m.FileName.Contains(moduleName));

        if (modules.Count() == 1)
        {
            var module = modules.First();
            Console.WriteLine("Found module: {0}, downloading to: {1}", module.FileName, directory.FullName);
            var fileInfo = new FileInfo(module.FileName);
            ushort dosHeaderMagic = dataTarget.DataReader.Read<ushort>(module.ImageBase);
            if (dosHeaderMagic != ExpectedDosHeaderMagic)
            {
                Console.WriteLine("Failed to read dos header");
                return;
            }
            
            var dosHeader = dataTarget.DataReader.Read<IMAGE_DOS_HEADER>(module.ImageBase);
            var ntHeader = dataTarget.DataReader.Read<IMAGE_NT_HEADERS64>(module.ImageBase + dosHeader.e_lfanew);
            if (ntHeader.Signature != 0x00004550)
            {
                Console.WriteLine("Unable to validate nt header");
                return;
            }
            ulong sectionAddrOffset = dosHeader.e_lfanew +
                                      (ulong)Marshal.OffsetOf(typeof(IMAGE_NT_HEADERS64), "OptionalHeader") +
                                      ntHeader.FileHeader.SizeOfOptionalHeader;

            ulong sectionAddr = module.ImageBase + sectionAddrOffset;
            ushort nSection = ntHeader.FileHeader.NumberOfSections;
            ulong dwEnd = module.ImageBase + ntHeader.OptionalHeader.SizeOfHeaders;

            MemLocation[] memLoc = new MemLocation[nSection];

            int indxSec = -1;
            int slot;

            for (int n = 0; n < nSection; n++)
            {
                IMAGE_SECTION_HEADER section = dataTarget.DataReader.Read<IMAGE_SECTION_HEADER>(sectionAddr);

                for (slot = 0; slot <= indxSec; slot++)
                {
                    if (section.PointerToRawData < (ulong)memLoc[slot].FileAddr)
                        break;
                }
                for (int k = indxSec; k >= slot; k--)
                    memLoc[k + 1] = memLoc[k];

                memLoc[slot].VAAddr = (IntPtr)section.VirtualAddress;
                memLoc[slot].VASize = (IntPtr)section.VirtualSize;
                memLoc[slot].FileAddr = (IntPtr)section.PointerToRawData;
                memLoc[slot].FileSize = (IntPtr)section.SizeOfRawData;

                indxSec++;
                sectionAddr += (ulong)Marshal.SizeOf(section);

            }

            int pageSize = 4096 ;

            byte[] buffer = new byte[pageSize];
            Span<byte> bufferSpan = new Span<byte>(buffer);

            using var fileStream = new FileStream($"{directory.FullName}\\{fileInfo.Name}", FileMode.Create, FileAccess.Write);
            int nRead;
            ulong dwAddr = module.ImageBase;
            while (dwAddr < dwEnd)
            {
                nRead = pageSize;
                if (dwEnd - dwAddr < (uint)nRead)
                    nRead = (int)(dwEnd - dwAddr);

                dataTarget.DataReader.Read(dwAddr, bufferSpan);
                fileStream.Write(buffer, 0, nRead);

                dwAddr += (uint)nRead;
            }

            for (slot = 0; slot <= indxSec; slot++)
            {
                dwAddr = module.ImageBase + (ulong)memLoc[slot].VAAddr;
                dwEnd = (ulong)memLoc[slot].FileSize + dwAddr - 1;

                while (dwAddr <= dwEnd)
                {
                    nRead = pageSize;
                    if (dwEnd - dwAddr + 1 < (uint)pageSize)
                        nRead = (int)(dwEnd - dwAddr + 1);

                    dataTarget.DataReader.Read(dwAddr, bufferSpan);
                    fileStream.Write(buffer, 0, nRead);

                    dwAddr += (uint)pageSize;
                }
            }
        }
        else
        {
            Console.WriteLine("Found more than one module that contains: {0}", moduleName);
            foreach (var moduleInfo in modules)
            {
                Console.WriteLine(moduleInfo.FileName);
            }
        }
    }
    
    [StructLayout(LayoutKind.Explicit)]
    public unsafe struct IMAGE_DOS_HEADER
    {
        [FieldOffset(0)]
        public UInt16 e_magic; // Magic number
        [FieldOffset(2)]
        public UInt16 e_cblp; // Bytes on last page of file
        [FieldOffset(4)]
        public UInt16 e_cp; // Pages in file
        [FieldOffset(6)]
        public UInt16 e_crlc; // Relocations
        [FieldOffset(8)]
        public UInt16 e_cparhdr; // Size of header in paragraphs
        [FieldOffset(10)]
        public UInt16 e_minalloc; // Minimum extra paragraphs needed
        [FieldOffset(12)]
        public UInt16 e_maxalloc; // Maximum extra paragraphs needed
        [FieldOffset(14)]
        public UInt16 e_ss; // Initial (relative) SS value
        [FieldOffset(16)]
        public UInt16 e_sp; // Initial SP value
        [FieldOffset(18)]
        public UInt16 e_csum; // Checksum
        [FieldOffset(20)]
        public UInt16 e_ip; // Initial IP value
        [FieldOffset(22)]
        public UInt16 e_cs; // Initial (relative) CS value
        [FieldOffset(24)]
        public UInt16 e_lfarlc; // File address of relocation table
        [FieldOffset(26)]
        public UInt16 e_ovno; // Overlay number
        [FieldOffset(28)]
        public fixed UInt16 e_res[4]; // Reserved words
        [FieldOffset(36)]
        public UInt16 e_oemid; // OEM identifier (for e_oeminfo)
        [FieldOffset(38)]
        public UInt16 e_oeminfo; // OEM information; e_oemid specific
        [FieldOffset(40)]
        public fixed UInt16 e_res2[10]; // Reserved words
        [FieldOffset(60)]
        public UInt32 e_lfanew; // File address of new exe header
    }
    
    [StructLayout(LayoutKind.Explicit)]
    public struct IMAGE_NT_HEADERS64
    {
        [FieldOffset(0)]
        public uint Signature;
        [FieldOffset(4)]
        public IMAGE_FILE_HEADER FileHeader;
        [FieldOffset(24)]
        public IMAGE_OPTIONAL_HEADER64 OptionalHeader;
    }
    
    [StructLayout(LayoutKind.Explicit)]
    public struct IMAGE_OPTIONAL_HEADER64
    {
        [FieldOffset(0)]
        public ushort Magic;
        [FieldOffset(2)]
        public byte MajorLinkerVersion;
        [FieldOffset(3)]
        public byte MinorLinkerVersion;
        [FieldOffset(4)]
        public UInt32 SizeOfCode;
        [FieldOffset(8)]
        public UInt32 SizeOfInitializedData;
        [FieldOffset(12)]
        public UInt32 SizeOfUninitializedData;
        [FieldOffset(16)]
        public UInt32 AddressOfEntryPoint;
        [FieldOffset(20)]
        public UInt32 BaseOfCode;
        [FieldOffset(24)]
        public UInt64 ImageBase;
        [FieldOffset(32)]
        public UInt32 SectionAlignment;
        [FieldOffset(36)]
        public UInt32 FileAlignment;
        [FieldOffset(40)]
        public ushort MajorOperatingSystemVersion;
        [FieldOffset(42)]
        public ushort MinorOperatingSystemVersion;
        [FieldOffset(44)]
        public ushort MajorImageVersion;
        [FieldOffset(46)]
        public ushort MinorImageVersion;
        [FieldOffset(48)]
        public ushort MajorSubsystemVersion;
        [FieldOffset(50)]
        public ushort MinorSubsystemVersion;
        [FieldOffset(52)]
        public UInt32 Win32VersionValue;
        [FieldOffset(56)]
        public UInt32 SizeOfImage;
        [FieldOffset(60)]
        public UInt32 SizeOfHeaders;
        [FieldOffset(64)]
        public UInt32 CheckSum;
        [FieldOffset(68)]
        public ushort Subsystem;
        [FieldOffset(70)]
        public ushort DllCharacteristics;
        [FieldOffset(72)]
        public UInt64 SizeOfStackReserve;
        [FieldOffset(80)]
        public UInt64 SizeOfStackCommit;
        [FieldOffset(88)]
        public UInt64 SizeOfHeapReserve;
        [FieldOffset(96)]
        public UInt64 SizeOfHeapCommit;
        [FieldOffset(104)]
        public UInt32 LoaderFlags;
        [FieldOffset(108)]
        public UInt32 NumberOfRvaAndSizes;
        [FieldOffset(112)]
        public IMAGE_DATA_DIRECTORY DataDirectory0;
        [FieldOffset(120)]
        public IMAGE_DATA_DIRECTORY DataDirectory1;
        [FieldOffset(128)]
        public IMAGE_DATA_DIRECTORY DataDirectory2;
        [FieldOffset(136)]
        public IMAGE_DATA_DIRECTORY DataDirectory3;
        [FieldOffset(144)]
        public IMAGE_DATA_DIRECTORY DataDirectory4;
        [FieldOffset(152)]
        public IMAGE_DATA_DIRECTORY DataDirectory5;
        [FieldOffset(160)]
        public IMAGE_DATA_DIRECTORY DataDirectory6;
        [FieldOffset(168)]
        public IMAGE_DATA_DIRECTORY DataDirectory7;
        [FieldOffset(176)]
        public IMAGE_DATA_DIRECTORY DataDirectory8;
        [FieldOffset(184)]
        public IMAGE_DATA_DIRECTORY DataDirectory9;
        [FieldOffset(192)]
        public IMAGE_DATA_DIRECTORY DataDirectory10;
        [FieldOffset(200)]
        public IMAGE_DATA_DIRECTORY DataDirectory11;
        [FieldOffset(208)]
        public IMAGE_DATA_DIRECTORY DataDirectory12;
        [FieldOffset(216)]
        public IMAGE_DATA_DIRECTORY DataDirectory13;
        [FieldOffset(224)]
        public IMAGE_DATA_DIRECTORY DataDirectory14;
        [FieldOffset(232)]
        public IMAGE_DATA_DIRECTORY DataDirectory15;

        public static unsafe IMAGE_DATA_DIRECTORY* GetDataDirectory(IMAGE_OPTIONAL_HEADER64* header, int zeroBasedIndex)
        {
            return (&header->DataDirectory0) + zeroBasedIndex;
        }
    }
    
    [StructLayout(LayoutKind.Sequential)]
    public struct IMAGE_DATA_DIRECTORY
    {
        public UInt32 VirtualAddress;
        public UInt32 Size;
    }
    
    [StructLayout(LayoutKind.Explicit)]
    public struct IMAGE_FILE_HEADER
    {
        [FieldOffset(0)]
        public UInt16 Machine;
        [FieldOffset(2)]
        public UInt16 NumberOfSections;
        [FieldOffset(4)]
        public UInt32 TimeDateStamp;
        [FieldOffset(8)]
        public UInt32 PointerToSymbolTable;
        [FieldOffset(12)]
        public UInt32 NumberOfSymbols;
        [FieldOffset(16)]
        public UInt16 SizeOfOptionalHeader;
        [FieldOffset(18)]
        public UInt16 Characteristics;
    }
   
    [StructLayout(LayoutKind.Sequential)]
    public struct MemLocation
    {
        public IntPtr VAAddr;
        public IntPtr VASize;
        public IntPtr FileAddr;
        public IntPtr FileSize;
    };
    
    unsafe internal struct IMAGE_SECTION_HEADER
    {
        public string Name
        {
            get
            {
                fixed (byte* ptr = NameBytes)
                {
                    if (ptr[7] == 0)
                        return System.Runtime.InteropServices.Marshal.PtrToStringAnsi((IntPtr)ptr);
                    else
                        return System.Runtime.InteropServices.Marshal.PtrToStringAnsi((IntPtr)ptr, 8);
                }
            }
        }
        public fixed byte NameBytes[8];
        public uint VirtualSize;
        public uint VirtualAddress;
        public uint SizeOfRawData;
        public uint PointerToRawData;
        public uint PointerToRelocations;
        public uint PointerToLinenumbers;
        public ushort NumberOfRelocations;
        public ushort NumberOfLinenumbers;
        public uint Characteristics;
    };
}