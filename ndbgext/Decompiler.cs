using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.CSharp;
using ICSharpCode.Decompiler.CSharp.OutputVisitor;
using ICSharpCode.Decompiler.IL;
using ICSharpCode.Decompiler.Metadata;
using ICSharpCode.Decompiler.TypeSystem;
using Microsoft.Diagnostics.Runtime;
using SequencePoint = ICSharpCode.Decompiler.DebugInfo.SequencePoint;

namespace ndbgext;

public class Decompiler
{
    private readonly DllExtractor _dllExtractor;
    private readonly DecompilerSettings _settings;

    public Decompiler(DllExtractor dllExtractor)
    {
        _settings = new DecompilerSettings
        {
            LoadInMemory = true,
            ThrowOnAssemblyResolveErrors = false
        };
        _dllExtractor = dllExtractor;
    }
    public string Decompile(ClrRuntime runtime, string filePath, ClrMethod method, IList<int> ilOffsets, string nextMethodName)
    {
        PEFile peFile = GetPeFile(runtime, filePath, method.Type);

        var decompiler = GetDecompiler(runtime, method.Type.Module, peFile, _settings);
        var typeDefinition = decompiler.TypeSystem.MainModule.Compilation.GetAllTypeDefinitions()
            .FirstOrDefault(t => t.MetadataToken.GetHashCode() == method.Type.MetadataToken);
        
        var ilSpyMethod = typeDefinition?.Methods.FirstOrDefault(m => m.MetadataToken.GetHashCode() == method.MetadataToken);
        if (ilSpyMethod != null)
        {
            var stringWriter = new StringWriter();
            var tokenWriter = TokenWriter.CreateWriterThatSetsLocationsInAST(stringWriter, "  ");
            var syntaxTree = decompiler.Decompile(ilSpyMethod.MetadataToken);
            syntaxTree.AcceptVisitor(new CSharpOutputVisitor(tokenWriter, _settings.CSharpFormattingOptions));
            var sequencePoints = decompiler.CreateSequencePoints(syntaxTree);
            var sw = new StringWriter();
            syntaxTree.AcceptVisitor(new CSharpOutputVisitor(sw, _settings.CSharpFormattingOptions));
            var decompiledMethod = sw.ToString();
            var split = decompiledMethod.Split('\n');

            var lineMatches = new List<LineMatch>(ilOffsets.Count);
            if (sequencePoints != null && sequencePoints.Count > 0)
            {
                //Since we are decompiling at the method level there should only be one set of sequence points
                var sps = sequencePoints.First();
                foreach (var offset in ilOffsets)
                {
                    var sp = FindSeqPointByOffset(offset, sps);
                    if (split.Length >= sp.StartLine)
                    {
                        if (split[sp.StartLine - 1].Contains(nextMethodName))
                        {
                            lineMatches.Add(new LineMatch{ lineNumber = sp.StartLine, methodNameMatches = true});
                        }
                        else
                        {
                            lineMatches.Add(new LineMatch{ lineNumber = sp.StartLine, methodNameMatches = false});
                        }
                    }
                }
                
                var moreThanOneOffset = lineMatches.Count > 1;
                var matchSymbol = moreThanOneOffset ? "**" : ">>";
                var nonMatchSymbol = moreThanOneOffset ? "??" : ">>";
                if (moreThanOneOffset)
                {
                    Console.WriteLine("More than one ilOffset was found for instruction pointer. Symbols:" +
                                      " ** (method name matches) ?? (method name does not match)");
                }

                foreach (var lineMatch in lineMatches)
                {
                    var symbol = lineMatch.methodNameMatches ? matchSymbol : nonMatchSymbol;
                    split[lineMatch.lineNumber - 1] = symbol + split[lineMatch.lineNumber - 1];
                }
                
                return string.Join("\n", split);
            }

            Console.WriteLine("Could not find source line for instruction pointer");
            return decompiledMethod;
        }

        Console.WriteLine("WARN: Method not found");
        return string.Empty;
    }

    private CSharpDecompiler GetDecompiler(ClrRuntime runtime, ClrModule module, PEFile peFile, DecompilerSettings settings)
    {
        var resolver = new DbgEngAssemblyResolver(new PeFileCache(_dllExtractor, runtime),
            peFile, settings, module.Name);
        var typeSystem = new DecompilerTypeSystem(peFile, resolver);
        var decompiler = new CSharpDecompiler(typeSystem, settings);
        return decompiler;
    }

    public string DecompileType(ClrRuntime runtime, string filePath, ClrType type)
    {
        var peFile = GetPeFile(runtime, filePath, type);

        var decompiler = GetDecompiler(runtime, type.Module, peFile, _settings);
        var typeDefinition = decompiler.TypeSystem.MainModule.Compilation.GetAllTypeDefinitions()
            .FirstOrDefault(t => t.MetadataToken.GetHashCode() == type.MetadataToken);

        var code = decompiler.DecompileType(typeDefinition.FullTypeName);
        return code.ToString();
    }

    private PEFile GetPeFile(ClrRuntime runtime, string filePath, ClrType type)
    {
        PEFile peFile;
        using (var memoryStream = new MemoryStream())
        {
            _dllExtractor.Extract(runtime.DataTarget.DataReader, type.Module.ImageBase, memoryStream);

            memoryStream.Seek(0, SeekOrigin.Begin);
            peFile = new PEFile(
                filePath,
                memoryStream,
                streamOptions: PEStreamOptions.PrefetchEntireImage,
                metadataOptions: new DecompilerSettings().ApplyWindowsRuntimeProjections
                    ? MetadataReaderOptions.ApplyWindowsRuntimeProjections
                    : MetadataReaderOptions.None);
        }

        return peFile;
    }

    private static SequencePoint? FindSeqPointByOffset(int ilOffset, KeyValuePair<ILFunction, List<SequencePoint>> first)
    {
        SequencePoint? result = null;
        foreach (var point in first.Value)
        {
            if (ilOffset >= point.Offset && ilOffset <= point.EndOffset)
            {
                result = point;
            }
        }

        return result;
    }

    struct LineMatch
    {
        public int lineNumber;
        public bool methodNameMatches;
    }
}
