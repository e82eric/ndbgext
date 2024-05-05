using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.Metadata;

namespace ndbgext;

sealed class DbgEngAssemblyResolver : IAssemblyResolver
{
    private readonly PeFileCache _peFileCache;
    private readonly string _targetFrameworkId;
    private readonly UniversalAssemblyResolver _universalAssemblyResolver;

    public DbgEngAssemblyResolver(
        PeFileCache peFileCache,
        PEFile peFile,
        DecompilerSettings settings,
        string filePath)
    {
        _targetFrameworkId = peFile.DetectTargetFrameworkId();
        _peFileCache = peFileCache;
        
        _universalAssemblyResolver = new UniversalAssemblyResolver(filePath, settings.ThrowOnAssemblyResolveErrors,
            _targetFrameworkId, peFile.DetectRuntimePack(),
            settings.LoadInMemory ? PEStreamOptions.PrefetchMetadata : PEStreamOptions.Default,
            settings.ApplyWindowsRuntimeProjections ? MetadataReaderOptions.ApplyWindowsRuntimeProjections : MetadataReaderOptions.None);
    }

    public PEFile Resolve(IAssemblyReference reference)
    {
        if (_peFileCache.TryGetByNameAndFrameworkId(reference.FullName, _targetFrameworkId, out var result))
        {
            return result;
        }

        if (_peFileCache.TryOpen(reference.Name, out result))
        {
            return result;
        }
        
        if (_peFileCache.TryGetFirstMatchByName(reference.FullName, out result))
        {
            return result;
        }
        
        var fromUniversal = _universalAssemblyResolver.Resolve(reference);
        if (fromUniversal != null)
        {
            return fromUniversal;
        }
            
        return null;
    }

    public PEFile ResolveModule(PEFile mainModule, string moduleName)
    {
        PEFile result = null;
        if (_peFileCache.TryGetByNameAndFrameworkId(moduleName, _targetFrameworkId, out result))
        {
            return result;
        }
        
        if (_peFileCache.TryOpen(moduleName, out result))
        {
            return result;
        }
            
        if (_peFileCache.TryGetFirstMatchByName(moduleName, out result))
        {
            return result;
        }
        
        var fromUniversal = _universalAssemblyResolver.ResolveModule(mainModule, moduleName);
        if (fromUniversal != null)
        {
            return fromUniversal;
        }
            
        return null;
    }
        
    public Task<PEFile> ResolveAsync(IAssemblyReference reference)
    {
        return Task.FromResult(Resolve(reference));
    }

    public Task<PEFile> ResolveModuleAsync(PEFile mainModule, string moduleName)
    {
        return Task.FromResult(ResolveModule(mainModule, moduleName));
    }
}