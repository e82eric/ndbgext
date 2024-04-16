using Microsoft.Diagnostics.Runtime;

namespace ndbgext;

public static class Helper
{
    static readonly Dictionary<ClrElementType, Func<ClrObject, string, string>> _objectTypeFunctions = new Dictionary<ClrElementType, Func<ClrObject, string, string>>
    {
        {ClrElementType.Int16, (type, fieldName) => type.ReadField<Int16>(fieldName).ToString() },
        {ClrElementType.Int32, (type, fieldName) => type.ReadField<Int32>(fieldName).ToString() },
        {ClrElementType.Int64, (type, fieldName) => type.ReadField<Int64>(fieldName).ToString() },
        {ClrElementType.UInt16, (type, fieldName) => type.ReadField<UInt16>(fieldName).ToString() },
        {ClrElementType.UInt32, (type, fieldName) => type.ReadField<UInt32>(fieldName).ToString() },
        {ClrElementType.UInt64, (type, fieldName) => type.ReadField<UInt64>(fieldName).ToString() },
        {ClrElementType.Boolean, (type, fieldName) => type.ReadField<Boolean>(fieldName).ToString() },
        {ClrElementType.Double, (type, fieldName) => type.ReadField<Double>(fieldName).ToString() },
    };

    public static bool IsNetCore(ClrRuntime runtime)
    {
        foreach (ClrModule module in runtime.EnumerateModules())
        {
            if (string.IsNullOrEmpty(module.AssemblyName))
                continue;

            var name = module.AssemblyName.ToLower();
            if (name.Contains("corelib"))
            {
                return true;
            }
        }

        return false;
    }
}