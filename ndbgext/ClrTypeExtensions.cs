using Microsoft.Diagnostics.Runtime;

namespace ndbgext;

public static class ClrTypeExtensions
{
    public static IReadOnlyList<(ClrType type, string implementName)> GetTypesThatImplement(ClrRuntime runtime, string searchString, bool contains)
    {
        var result = new List<(ClrType, string name)>();
        foreach (var module in runtime.EnumerateModules())
        {
            foreach ((ulong mt, int _) in module.EnumerateTypeDefToMethodTableMap())
            {
                ClrType? type = runtime.GetTypeByMethodTable(mt);
                if (type != null && type.Name != null)
                {
                    foreach (var clrInterface in type.EnumerateInterfaces())
                    {
                        if (contains)
                        {
                            if (clrInterface.Name.Contains(searchString))
                            {
                                result.Add((type, clrInterface.Name));
                            }
                        }
                        else
                        {
                            if (clrInterface.Name == searchString)
                            {
                                result.Add((type, clrInterface.Name));
                            }
                        }
                        
                        ClrInterface? baseInterface = clrInterface.BaseInterface;
                        while (baseInterface != null)
                        {
                            if (contains)
                            {
                                if (baseInterface.Name.Contains(searchString))
                                {
                                    result.Add((type, baseInterface.Name));
                                }
                            }
                            else
                            {
                                if (baseInterface.Name == searchString)
                                {
                                    result.Add((type, baseInterface.Name));
                                }
                            }
                            
                            baseInterface = baseInterface.BaseInterface;
                        }
                    }

                    ClrType? baseType = type.BaseType;
                    while (baseType != null)
                    {
                        if (contains)
                        {
                            if (baseType.Name != null && baseType.Name.ToLower().Contains(searchString.ToLower()))
                            {
                                result.Add((type, baseType.Name));
                            }
                        }
                        else
                        {
                            if (baseType.Name == searchString)
                            {
                                result.Add((type, baseType.Name));
                            }
                        }

                        foreach (var clrInterface in baseType.EnumerateInterfaces())
                        {
                            if (contains)
                            {
                                if (clrInterface.Name.ToLower().Contains(searchString.ToLower()))
                                {
                                    result.Add((type, clrInterface.Name));
                                }
                            }
                            else
                            {
                                if (clrInterface.Name == searchString)
                                {
                                    result.Add((type, clrInterface.Name));
                                }
                            }
                            
                            ClrInterface? baseInterface = clrInterface.BaseInterface;
                            while (baseInterface != null)
                            {
                                if (contains)
                                {
                                    if (baseInterface.Name.Contains(searchString))
                                    {
                                        result.Add((type, baseInterface.Name));
                                    }
                                }
                                else
                                {
                                    if (baseInterface.Name == searchString)
                                    {
                                        result.Add((type, baseInterface.Name));
                                    }
                                }
                            
                                baseInterface = baseInterface.BaseInterface;
                            }
                        }

                        baseType = baseType.BaseType;
                    }
                }
            }
        }

        return result;
    }
}