using System.Collections.Concurrent;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using ICSharpCode.Decompiler.Metadata;

namespace Decompiler;
sealed class MyAssemblyResolver : IAssemblyResolver
{
    private readonly PeFileCache _peFileCache;
    private readonly string _targetFrameworkId;
        
    private readonly UniversalAssemblyResolver _universalAssemblyResolver;

    public MyAssemblyResolver(
        PeFileCache peFileCache,
        string targetFrameworkId,
        UniversalAssemblyResolver universalAssemblyResolver)
    {
        _targetFrameworkId = targetFrameworkId;
        _universalAssemblyResolver = universalAssemblyResolver;
        _peFileCache = peFileCache;
    }

    public PEFile Resolve(IAssemblyReference reference)
    {
        // return _universalAssemblyResolver.Resolve(reference);
        PEFile result = null;
        if (_peFileCache.TryGetByNameAndFrameworkId(reference.FullName, _targetFrameworkId, out result))
        {
            return result;
        }
            
        string file = _universalAssemblyResolver.FindAssemblyFile(reference);
        if (file != null)
        {
            if (_peFileCache.TryOpen(file, out result))
            {
                return result;
            }
            return null;
        }
        if (_peFileCache.TryGetFirstMatchByName(reference.FullName, out result))
        {
            return result;
        }
            
        return null;
    }

    public PEFile ResolveModule(PEFile mainModule, string moduleName)
    {
        // return _universalAssemblyResolver.ResolveModule(mainModule, moduleName);
        PEFile result = null;
        if (_peFileCache.TryGetByNameAndFrameworkId(moduleName, _targetFrameworkId, out result))
        {
            return result;
        }
            
        var moduleFromResolver = _universalAssemblyResolver.ResolveModule(mainModule, moduleName);
        if (moduleFromResolver != null)
        {
            return moduleFromResolver;
        }
            
        string file = Path.Combine(Path.GetDirectoryName(mainModule.FileName), moduleName);
        if (File.Exists(file))
        {
            if (_peFileCache.TryOpen(file, out result))
            {
                return result;
            }

            return null;
        }
        if (_peFileCache.TryGetFirstMatchByName(moduleName, out result))
        {
            return result;
        }
            
        return null;
    }
        
    public Task<PEFile> ResolveAsync(IAssemblyReference reference)
    {
        var result = Task.Run(() => Resolve(reference));
        return result;
    }

    public Task<PEFile> ResolveModuleAsync(PEFile mainModule, string moduleName)
    {
        var result = Task.Run(() => ResolveModule(mainModule, moduleName));
        return result;
    }
}

public class PeFileCache
{
    private readonly ConcurrentDictionary<string, string> _byFileName = new();
    private readonly ConcurrentDictionary<string, PEFile> _peFileCache = new();

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
            
        using (var fileStream = new FileStream(fileName, FileMode.Open, FileAccess.Read))
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
            var options = MetadataReaderOptions.ApplyWindowsRuntimeProjections;

            peFile = new PEFile(fileName, stream, streamOptions, metadataOptions: options);

            return true;
        }
        catch (Exception)
        {
            //TODO: Log something here
            return false;
        }
    }
}
