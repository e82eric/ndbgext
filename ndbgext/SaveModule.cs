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
    private readonly DllExtractor _dllExtractor;

    public SaveModuleProvider(DllExtractor dllExtractor)
    {
        _dllExtractor = dllExtractor;
    }
    
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
            var filePath = $"{directory.FullName}\\{fileInfo.Name}";
            using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write | FileAccess.Read);
            _dllExtractor.Extract(dataTarget.DataReader, module.ImageBase, fileStream);
            Console.WriteLine(filePath);
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
}