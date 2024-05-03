using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.CSharp;
using ICSharpCode.Decompiler.CSharp.OutputVisitor;
using ICSharpCode.Decompiler.Metadata;
using ICSharpCode.Decompiler.TypeSystem;
using Microsoft.Diagnostics.Runtime;

namespace ndbgext;

public class Decompiler
{
    public string Decompile(string filePath, Stream stream, ClrMethod method, int ilOffset)
    {
        stream.Seek(0, SeekOrigin.Begin);
        var peFile = new PEFile(
            filePath,
            stream,
            streamOptions: PEStreamOptions.PrefetchEntireImage,
            metadataOptions: new DecompilerSettings().ApplyWindowsRuntimeProjections ? MetadataReaderOptions.ApplyWindowsRuntimeProjections : MetadataReaderOptions.None );

        var settings = new DecompilerSettings
        {
            LoadInMemory = true,
            ThrowOnAssemblyResolveErrors = false
        };
        var resolver = new UniversalAssemblyResolver(filePath, settings.ThrowOnAssemblyResolveErrors,
            peFile.DetectTargetFrameworkId(), peFile.DetectRuntimePack(),
            settings.LoadInMemory ? PEStreamOptions.PrefetchMetadata : PEStreamOptions.Default,
            settings.ApplyWindowsRuntimeProjections ? MetadataReaderOptions.ApplyWindowsRuntimeProjections : MetadataReaderOptions.None);
        var typeSystem = new DecompilerTypeSystem(peFile, resolver);
        var decompiler = new CSharpDecompiler(typeSystem, settings);
        var typeDefinition = decompiler.TypeSystem.MainModule.Compilation.FindType(new FullTypeName(method.Type.Name)).GetDefinition();
        var ilSpyMethod = typeDefinition.Methods.FirstOrDefault(m => m.MetadataToken.GetHashCode() == method.MetadataToken);

        if (ilSpyMethod != null)
        {
            var stringWriter = new StringWriter();
            var tokenWriter = TokenWriter.CreateWriterThatSetsLocationsInAST(stringWriter, "  ");
            var syntaxTree = decompiler.Decompile(ilSpyMethod.MetadataToken);
            syntaxTree.AcceptVisitor(new CSharpOutputVisitor(tokenWriter, settings.CSharpFormattingOptions));
            var sequencePoints = decompiler.CreateSequencePoints(syntaxTree);
            var first = sequencePoints.First();

            ICSharpCode.Decompiler.DebugInfo.SequencePoint? found = null;
            foreach (ICSharpCode.Decompiler.DebugInfo.SequencePoint? point in first.Value)
            {
                if (ilOffset >= point.Offset && ilOffset <= point.EndOffset)
                {
                    found = point;
                }
            }

            if (found != null)
            {
                Console.WriteLine("Line {0} StartChar {1}", found.StartLine, found.StartColumn);
            }

            var sw = new StringWriter();
            syntaxTree.AcceptVisitor(new CSharpOutputVisitor(sw, settings.CSharpFormattingOptions));
            var decompiledMethod = sw.ToString();

            var split = decompiledMethod.Split('\n');
            split[found.StartLine - 1] = ">>" + split[found.StartLine - 1];
            
            return string.Join("\n", split);
        }
        return string.Empty;
    }
}
