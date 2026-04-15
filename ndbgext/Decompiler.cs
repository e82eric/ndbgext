using System.Diagnostics.CodeAnalysis;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.CSharp;
using ICSharpCode.Decompiler.CSharp.OutputVisitor;
using ICSharpCode.Decompiler.CSharp.Syntax;
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
    
    public string DecompileMethodWithCurrentLineIndicator(ClrRuntime runtime, ClrMethod method, IList<int> ilOffsets, string? nextMethodName)
    {
        if (TryDecompileMethod(runtime, method, out var syntaxTree, out var decompiler))
        {
            var decompiledMethod = RenderSyntaxTree(syntaxTree);
            var split = decompiledMethod.Split('\n');
            Dictionary<ILFunction, List<SequencePoint>>? sequencePoints = null;

            try
            {
                var stringWriter = new StringWriter();
                var tokenWriter = TokenWriter.CreateWriterThatSetsLocationsInAST(stringWriter, "  ");
                syntaxTree.AcceptVisitor(new CSharpOutputVisitor(tokenWriter, _settings.CSharpFormattingOptions));
                sequencePoints = decompiler.CreateSequencePoints(syntaxTree);
            }
            catch (Exception e)
            {
                Console.WriteLine("WARN: Could not map IL offsets to source lines: {0}", e.Message);
                return decompiledMethod;
            }

            var lineMatches = new List<LineMatch>(ilOffsets.Count);
            if (sequencePoints != null && sequencePoints.Count > 0)
            {
                //Since we are decompiling at the method level there should only be one set of sequence points
                var sps = sequencePoints.First();
                foreach (var offset in ilOffsets)
                {
                    var sp = FindSeqPointByOffset(offset, sps);
                    if (sp != null && split.Length >= sp.StartLine)
                    {
                        if (nextMethodName != null && split[sp.StartLine - 1].Contains(nextMethodName))
                        {
                            lineMatches.Add(new LineMatch{ LineNumber = sp.StartLine, MethodNameMatches = true});
                        }
                        else
                        {
                            lineMatches.Add(new LineMatch{ LineNumber = sp.StartLine, MethodNameMatches = false});
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
                    var symbol = lineMatch.MethodNameMatches ? matchSymbol : nonMatchSymbol;
                    split[lineMatch.LineNumber - 1] = symbol + split[lineMatch.LineNumber - 1];
                }
                
                return string.Join("\n", split);
            }

            Console.WriteLine("Could not find source line for instruction pointer");
            return decompiledMethod;
        }

        if (TryDecompileMethodFromType(runtime, method, out var typeDecompiledMethod))
        {
            Console.WriteLine("WARN: Fell back to type-level decompilation for method {0}.{1}", method.Type?.Name, method.Name);
            return typeDecompiledMethod;
        }

        Console.WriteLine("WARN: Method not found");
        return string.Empty;
    }
    
    public string DecompileMethod(ClrRuntime runtime, ClrMethod method)
    {
        if (TryDecompileMethod(runtime, method, out var syntaxTree, out _))
        {
            return RenderSyntaxTree(syntaxTree);
        }
        if (TryDecompileMethodFromType(runtime, method, out var typeDecompiledMethod))
        {
            Console.WriteLine("WARN: Fell back to type-level decompilation for method {0}.{1}", method.Type?.Name, method.Name);
            return typeDecompiledMethod;
        }
        Console.WriteLine("WARN: Method not found");
        return string.Empty;
    }
    
    private bool TryDecompileMethod(ClrRuntime runtime, ClrMethod method, [NotNullWhen(true)]out SyntaxTree? syntaxTree, [NotNullWhen(true)]out CSharpDecompiler? decompiler)
    {
        syntaxTree = null;
        decompiler = null;
        if (method.Type?.Module?.Name == null)
        {
            return false;
        }

        Console.WriteLine("Type: {0}", method.Type.Name);

        var peFile = GetPeFile(runtime, method.Type.Module.Name, method.Type.Module.ImageBase);
        decompiler = GetDecompiler(runtime, method.Type.Module.Name, peFile, _settings);

        var ilSpyMethod = FindMethodDefinition(decompiler, method);
        if (ilSpyMethod == null)
        {
            return false;
        }

        try
        {
            syntaxTree = decompiler.Decompile(ilSpyMethod.MetadataToken);
            return true;
        }
        catch (Exception e)
        {
            Console.WriteLine("WARN: Method-level decompilation failed for {0}.{1}: {2}", method.Type?.Name, method.Name, e.Message);
            syntaxTree = null;
            return false;
        }
    }

    private CSharpDecompiler GetDecompiler(ClrRuntime runtime, string moduleName, PEFile peFile, DecompilerSettings settings)
    {
        var resolver = new DbgEngAssemblyResolver(new PeFileCache(_dllExtractor, runtime), peFile, settings, moduleName);
        var typeSystem = new DecompilerTypeSystem(peFile, resolver);
        var decompiler = new CSharpDecompiler(typeSystem, settings);
        return decompiler;
    }
    
    public string DecompileType(ClrRuntime runtime, string fullTypeName)
    {
        foreach (var module in runtime.EnumerateModules())
        {
            if (module.Name != null)
            {
                var peFile = GetPeFile(runtime, module.Name,  module.ImageBase);
                var typeDefinition = peFile.GetTypeDefinition(new TopLevelTypeName(fullTypeName));
                if (!typeDefinition.IsNil)
                {
                    var decompiler = GetDecompiler(runtime, module.Name, peFile, _settings);
                    var code = decompiler.Decompile(typeDefinition);
                    return code.ToString();
                }
            }
        }

        return string.Empty;
    }

    public string DecompileType(ClrRuntime runtime, string filePath, ClrType type)
    {
        if (type.Module.Name == null)
        {
            return string.Empty;
        }

        var peFile = GetPeFile(runtime, filePath, type.Module.ImageBase);
        var decompiler = GetDecompiler(runtime, type.Module.Name, peFile, _settings);
        var typeDefinition = decompiler.TypeSystem.MainModule.Compilation.GetAllTypeDefinitions()
            .FirstOrDefault(t => t.MetadataToken.GetHashCode() == type.MetadataToken);

        if (typeDefinition != null)
        {
            var code = decompiler.Decompile(typeDefinition.MetadataToken);
            return code.ToString();
        }

        return string.Empty;
    }

    private PEFile GetPeFile(ClrRuntime runtime, string filePath, ulong imageBase)
    {
        PEFile peFile;
        using (var memoryStream = new MemoryStream())
        {
            _dllExtractor.Extract(runtime.DataTarget.DataReader, imageBase, memoryStream);

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
        public int LineNumber;
        public bool MethodNameMatches;
    }

    private IMethod? FindMethodDefinition(CSharpDecompiler decompiler, ClrMethod method)
    {
        var allTypeDefinitions = decompiler.TypeSystem.MainModule.Compilation.GetAllTypeDefinitions();
        var typeDefinition = allTypeDefinitions.FirstOrDefault(t => t.MetadataToken.GetHashCode() == method.Type.MetadataToken);
        typeDefinition ??= allTypeDefinitions.FirstOrDefault(t =>
            string.Equals(t.FullName, method.Type.Name, StringComparison.Ordinal));

        if (typeDefinition == null)
        {
            return null;
        }

        var methodDefinition = typeDefinition.Methods.FirstOrDefault(m => m.MetadataToken.GetHashCode() == method.MetadataToken);
        methodDefinition ??= typeDefinition.Methods.FirstOrDefault(m =>
            string.Equals(m.Name, method.Name, StringComparison.Ordinal));
        return methodDefinition;
    }

    private string RenderSyntaxTree(SyntaxTree syntaxTree)
    {
        var sw = new StringWriter();
        syntaxTree.AcceptVisitor(new CSharpOutputVisitor(sw, _settings.CSharpFormattingOptions));
        return sw.ToString();
    }

    private bool TryDecompileMethodFromType(ClrRuntime runtime, ClrMethod method, [NotNullWhen(true)] out string? code)
    {
        code = null;
        if (method.Type?.Module?.Name == null || method.Type?.Name == null || string.IsNullOrEmpty(method.Name))
        {
            return false;
        }

        try
        {
            var typeCode = DecompileType(runtime, method.Type.Module.Name, method.Type);
            if (string.IsNullOrWhiteSpace(typeCode))
            {
                return false;
            }

            code = MarkMethodInTypeDecompilation(typeCode, method.Name);
            return true;
        }
        catch (Exception e)
        {
            Console.WriteLine("WARN: Type-level decompilation failed for {0}.{1}: {2}", method.Type.Name, method.Name, e.Message);
            return false;
        }
    }

    private static string MarkMethodInTypeDecompilation(string typeCode, string methodName)
    {
        var lines = typeCode.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].Contains(methodName, StringComparison.Ordinal))
            {
                lines[i] = ">>" + lines[i];
                break;
            }
        }

        return string.Join("\n", lines);
    }
}
