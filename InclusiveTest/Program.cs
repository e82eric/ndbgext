using System;
using System.Collections.Generic;

namespace InclusiveTest
{
    internal class Program
    {
        public class BigObject
        {
            public byte[] bytes = new byte[500000];
        }

        public static void Main(string[] args)
        {
            var bigObjects = new List<BigObject>();
            for (var i = 0; i < 10; i++)
            {
                bigObjects.Add(new BigObject());
            }
            Console.ReadLine();
        }
    }
}