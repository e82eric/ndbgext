using System.Globalization;
using Microsoft.Diagnostics.Runtime;

namespace ndbgext;

public static class Helper
{
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
        if (!itemObj.TryReadObjectField("m_action", out callback))
        {
            if (!itemObj.TryReadObjectField("_callback", out callback))
            {
                if(!itemObj.TryReadObjectField("callback", out callback))
                {
                    result = "[no callback]";
                    return result;
                }
            }
        }

        ClrObject target;
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
                || target.Type != null && target.Type.Name != null && target.Type.Name.StartsWith("System.Action<"))
            {
                result = $"{method.Type.Name}.{method.Name}";
            }
            else
            {
                result = $"{target.Type?.Name}.{method.Type.Name}.{method.Name}";
            }
        }

        return result;
    }
    
    public static string FormatBytes(float bytes)
    {
        const int scale = 1024;
        string[] units = { "bytes", "KB", "MB", "GB", "TB", "PB", "EB" }; // Extend with more units if needed.
    
        if (bytes == 0)
        {
            return "0 bytes";
        }
    
        int unitIndex = 0;
        double displayValue = bytes;
        while (displayValue >= scale && unitIndex < units.Length - 1)
        {
            displayValue /= scale;
            unitIndex++;
        }
    
        return $"{displayValue:N2} {units[unitIndex]}";
    }

    public static bool TryParseAddress(string value, out ulong result)
    {
        if (value == "0")
        {
            result = 0;
            return true;
        }
        
        result = default;
        if (value.StartsWith("0x"))
        {
            // remove "0x" for parsing
            value = value.Substring(2).TrimStart('0');
        }

        // remove the leading 0000 that WinDBG often add in 64 bit
        value = value.TrimStart('0');

        return ulong.TryParse(value, NumberStyles.HexNumber,
            CultureInfo.InvariantCulture, out result);
    }
    
    public static bool TryParseToken(string value, out int result)
    {
        result = default;
        if (value.StartsWith("0x"))
        {
            // remove "0x" for parsing
            value = value.Substring(2).TrimStart('0');
        }

        // remove the leading 0000 that WinDBG often add in 64 bit
        value = value.TrimStart('0');

        return int.TryParse(value, NumberStyles.HexNumber,
            CultureInfo.InvariantCulture, out result);
    }
    
    public static IEnumerable<ulong> EnumeratePointersInRange(this ClrRuntime runtime, ulong start, ulong stop)
    {
        uint diff = (uint)runtime.DataTarget.DataReader.PointerSize;

        if (start > stop)
            for (ulong ptr = stop; ptr <= start; ptr += diff)
                yield return ptr;
        else
            for (ulong ptr = stop; ptr >= start; ptr -= diff)
                yield return ptr;
    }

    public static void WritePlainText(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return;

        var normalized = text.Replace("\r\n", "\n");
        var endsWithNewline = normalized.EndsWith('\n');
        var lines = normalized.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (i == lines.Length - 1 && lines[i].Length == 0 && endsWithNewline)
                break;

            Console.WriteLine("{0}", lines[i].Replace("%", "%%"));
        }
    }
}
