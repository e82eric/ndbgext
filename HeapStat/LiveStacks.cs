using System;
using System.Collections.Generic;
using System.Threading;

namespace LiveStacks
{
    public static class LiveStacks
    {
        public static void Run(int processId, TimeSpan duration, int numberOfStacks, int minimumSamples)
        {
            var resolver = new ProcessStackResolver(processId);
            var session = new LiveSession(processId);
            _ = new Timer(_ => { 
                session.Stop();
            }, null, duration, duration);
            
            session.Start();
            
            var stacks = session.Stacks.TopStacks(numberOfStacks, minimumSamples);
            foreach (var stack in stacks)
            {
                PrintNormalStack(stack, resolver);
            }
        }
        
        private static void PrintNormalStack(KeyValuePair<OneStack, int> stack, ProcessStackResolver resolver)
        {
            Console.WriteLine($"  {stack.Value}");
            foreach (var symbol in resolver.Resolve(stack.Key.Addresses))
            {
                Console.WriteLine("    " + symbol.ToString());
            }
            Console.WriteLine();
        }
    }
}