using System.Text;
using DbgEngExtension;
using Microsoft.Diagnostics.Runtime.Utilities.DbgEng;

namespace ndbgext;

public static class ResultStore
{
    private static readonly Dictionary<string, string> BackingStore = new();
    public static void Put(string key, string results) => BackingStore[key] = results;
    public static string? Get(string key) => BackingStore.GetValueOrDefault(key);
}

public class StoreCommand : DbgEngCommand
{
    class StringOutputCallbacks : IDebugOutputCallbacks
    {
        private readonly StringBuilder _sb = new();

        public string Result() => _sb.ToString();
        public void OnText(DEBUG_OUTPUT flags, string? text, ulong args)
        {
            _sb.Append(text);
        }
    }
    
    public StoreCommand(nint pUnknown, bool redirectConsoleOutput = true)
        : base(pUnknown, redirectConsoleOutput)
    {
    }

    internal void Run(string args)
    {
        var arguments = args.Split(' ');

        if (arguments.Length < 2)
        {
            PrintUsage();
            return;
        }

        if (arguments[0] == "-put")
        {
            if (arguments.Length < 4)
            {
                PrintUsage();
                return;
            }

            var variable = arguments[1];
            var op = arguments[2];
            if (op != "-command")
            {
                PrintUsage();
                return;
            }

            var command = String.Join(' ', arguments[3..]);
            Console.WriteLine("Storing results of '{0}' to '{1}'", command, variable);
            var outputCapture = new StringOutputCallbacks();
            var oldCaptures = DebugClient.GetOutputCallbacks();
            DebugClient.SetOutputCallbacks(outputCapture);
            var executeResult = DebugControl.Execute(
                DEBUG_OUTCTL.NOT_LOGGED | DEBUG_OUTCTL.THIS_CLIENT,
                command,
                DEBUG_EXECUTE.DEFAULT);

            DebugClient.SetOutputCallbacks(oldCaptures);
            
            if(executeResult != 0)
            {
                throw new Exception($"Executing '{command}' failed");
            }
            
            ResultStore.Put(variable, outputCapture.Result());
            
            Console.WriteLine("Saved result to {0}", variable);
        }
        else if (arguments[0] == "-get" && arguments.Length == 2)
        {
            var result = ResultStore.Get(arguments[1]);
            Console.WriteLine(result);
        }
        else
        {
            PrintUsage();
        }
    }

    private void PrintUsage()
    {
        Console.WriteLine("!tstore -put VARIABLE_NAME -command TEXT");
        Console.WriteLine("!tstore -get VARIABLE_NAME");
    }
}