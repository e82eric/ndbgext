using System.Collections.Concurrent;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.Metadata;
using Microsoft.Diagnostics.Runtime;

namespace ndbgext;

public class PeFileCache
{
    private readonly DllExtractor _extractor;
    private readonly ClrRuntime _clrRuntime;
    private readonly ConcurrentDictionary<string, string> _byFileName = new();
    private readonly ConcurrentDictionary<string, PEFile> _peFileCache = new();

    public PeFileCache(DllExtractor extractor, ClrRuntime clrRuntime)
    {
        _extractor = extractor;
        _clrRuntime = clrRuntime;
    }

    public PEFile[] GetAssemblies()
    {
        var result = _peFileCache.Values.ToArray();
        return result;
    }

    public int GetAssemblyCount()
    {
        return _peFileCache.Count;
    }
    
    public bool TryGetByNameAndFrameworkId(string fullName, string targetFrameworkId, out PEFile peFile)
    {
        var uniqueness = fullName + '|' + targetFrameworkId;
        if (_peFileCache.TryGetValue(uniqueness, out peFile))
        {
            return true;
        }
        
        return false;
    }
        
    public bool TryGetFirstMatchByName(string fullName, out PEFile peFile)
    {
        peFile = null;
        var firstFulNameMatch = _peFileCache.Keys.FirstOrDefault(k => k.StartsWith(fullName));
        if(firstFulNameMatch == null)
        {
            return false;
        }
        if (_peFileCache.TryGetValue(firstFulNameMatch, out peFile))
        {
            return true;
        }

        return false;
    }
        
    public bool TryOpen(string fileName, out PEFile peFile)
    {
        if (_byFileName.TryGetValue(fileName, out var uniqueness))
        {
            if (_peFileCache.TryGetValue(uniqueness, out peFile))
            {
                return true;
            }
        }
            
        using (var fileStream = new MemoryStream())
        {
            if(TryLoadAssembly(fileStream, PEStreamOptions.PrefetchEntireImage, fileName, out peFile))
            {
                var targetFrameworkId = peFile.DetectTargetFrameworkId();
                uniqueness = peFile.FullName + '|' + targetFrameworkId;
                _byFileName.TryAdd(fileName, uniqueness);
                _peFileCache.TryAdd(uniqueness, peFile);
                return true;
            }
            return false;
        }
    }
    
    private bool TryLoadAssembly(Stream stream, PEStreamOptions streamOptions, string fileName, out PEFile peFile)
    {
        peFile = null;
        try
        {
            ClrModule? module = null;
            foreach (var clrModule in _clrRuntime.EnumerateModules())
            {
                var moduleFileName = new FileInfo(clrModule.Name).Name;
                var nameWithoutExtension = Path.GetFileNameWithoutExtension(moduleFileName);
                if (fileName == nameWithoutExtension)
                {
                    module = clrModule;
                    break;
                }
            }
            if (module != null)
            {
                using (MemoryStream memoryStream = new())
                {
                    _extractor.Extract(_clrRuntime.DataTarget.DataReader, module.ImageBase, memoryStream);
                
                    peFile = new PEFile(
                        module.Name,
                        memoryStream,
                        streamOptions: PEStreamOptions.PrefetchEntireImage,
                        metadataOptions: new DecompilerSettings().ApplyWindowsRuntimeProjections ? MetadataReaderOptions.ApplyWindowsRuntimeProjections : MetadataReaderOptions.None );
                }

                return true;
            }

            return false;
        }
        catch (Exception)
        {
            //TODO: Log something here
            return false;
        }
    }
}