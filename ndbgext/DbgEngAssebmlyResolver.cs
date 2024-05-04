using ICSharpCode.Decompiler.Metadata;

namespace ndbgext;

sealed class DbgEngAssemblyResolver : IAssemblyResolver
{
    private readonly PeFileCache _peFileCache;
    private readonly string _targetFrameworkId;
        
    public DbgEngAssemblyResolver(
        PeFileCache peFileCache,
        string targetFrameworkId)
    {
        _targetFrameworkId = targetFrameworkId;
        _peFileCache = peFileCache;
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