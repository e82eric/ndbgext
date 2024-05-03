using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.CSharp;
using ICSharpCode.Decompiler.CSharp.OutputVisitor;
using ICSharpCode.Decompiler.Metadata;
using ICSharpCode.Decompiler.TypeSystem;

namespace Decompiler;

class Program
{
    static void Main(string[] args)
    {
        var filePath = @"C:\users\eric\CsDecompileLib.dll";
        using
        var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
        
        var peFile = new PEFile(
            filePath,
            fileStream,
            streamOptions: PEStreamOptions.PrefetchEntireImage,
            metadataOptions: new DecompilerSettings().ApplyWindowsRuntimeProjections ? MetadataReaderOptions.ApplyWindowsRuntimeProjections : MetadataReaderOptions.None );

        var settings = new DecompilerSettings();
        settings.LoadInMemory = true;
        var file = peFile;
        var universalAssemblyResolver = new UniversalAssemblyResolver(filePath,
            false,
            file.DetectTargetFrameworkId(),
            file.DetectRuntimePack(),
            settings.LoadInMemory ? PEStreamOptions.PrefetchMetadata : PEStreamOptions.Default,
            settings.ApplyWindowsRuntimeProjections ? MetadataReaderOptions.ApplyWindowsRuntimeProjections : MetadataReaderOptions.None);
        var resolver = new MyAssemblyResolver(new PeFileCache(), peFile.DetectTargetFrameworkId(), universalAssemblyResolver);
        var typeSystem = new DecompilerTypeSystem(file, resolver);
        var types = typeSystem.FindType(new FullTypeName("CsDecompileLib.Nuget.AddNugetPackageHandler"));
        var method = types.GetMethods(m => m.MetadataToken.GetHashCode() == 100663472).First();
        var decompiler = new CSharpDecompiler(typeSystem, settings);
        var stringWriter = new StringWriter();
        var tokenWriter = TokenWriter.CreateWriterThatSetsLocationsInAST(stringWriter, "  ");
        var syntaxTree = decompiler.DecompileType(new FullTypeName(types.FullName));
        syntaxTree.AcceptVisitor(new CSharpOutputVisitor(tokenWriter, settings.CSharpFormattingOptions));
        //var syntaxTree = decompiler.Decompile(method.MetadataToken);
        var sequencePoints = decompiler.CreateSequencePoints(syntaxTree);
        
        string code = decompiler.DecompileWholeModuleAsString();
        //var decompiler = new CSharpDecompiler(filePath, new DecompilerSettings());
        //string code = decompiler.DecompileWholeModuleAsString();
        //var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
        //    
        //var peFile = new PEFile(filePath, stream, streamOptions: PEStreamOptions.PrefetchMetadata);
        //var resolver = new UniversalAssemblyResolver(peFile.Name, false, peFile.DetectRuntimePack());

        //// Create decompiler settings (optional)
        //var settings = new DecompilerSettings();
        //    
        //// Create the decompiler with the PE file and resolver
        //var decompiler = new CSharpDecompiler(peFile, resolver, settings);

        //// Decompile the entire assembly
        //var code = decompiler.DecompileWholeModuleAsString();
            
        Console.WriteLine(code);
        Console.ReadLine();
    }
}