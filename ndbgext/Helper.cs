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

    public static string GetDelegateMethod(ClrRuntime runtime, ClrObject itemObj)
    {
        ClrObject callback;
        var result = string.Empty;
        if (itemObj.Type.Name == "System.Threading.Tasks.Task")
        {
            callback = itemObj.ReadObjectField("m_action");
            //result.Type = ThreadRoot.Task;
        }
        else
        {
            if (!itemObj.TryReadObjectField("_callback", out callback))
            {
                itemObj.TryReadObjectField("callback", out callback);
            }

            if (!callback.IsNull)
            {
                //result.Type = ThreadRoot.WorkItem;
            }
            else
            {
                result = "[no callback]";
                return result;
            }
        }

        ClrObject target = default(ClrObject);
        if (!callback.TryReadObjectField("_target", out target))
        {
            result = "[no callback target]";
            return result;
        }

        if (target.Type == null)
        {
            //Is this going to be in hex?
            result = $"[target=0x{(ulong)target}";
            return result;
        }

        var methodPtrVal = callback.ReadField<ulong>("_methodPtr");
        var method = runtime.GetMethodByInstructionPointer(methodPtrVal);
        if (method == null)
        {
            var methodPtrAuxVal = callback.ReadField<ulong>("_methodPtrAux");
            method = runtime.GetMethodByInstructionPointer(methodPtrAuxVal);
        }

        if (method != null)
        {
            // anonymous method
            if (method.Type.Name == target.Type.Name)
            {
                result = $"{target.Type.Name}.{method.Name}";
            }
            // method is implemented by an class inherited from targetType
            // ... or a simple delegate indirection to a static/instance method
            else if(target.Type.Name == "System.Threading.WaitCallback"
                || target.Type.Name.StartsWith("System.Action<"))
            {
                result = $"{method.Type.Name}.{method.Name}";
            }
            else
            {
                result = $"{target.Type.Name}.{method.Type.Name}.{method.Name}";
            }
        }

        return result;
    }
}