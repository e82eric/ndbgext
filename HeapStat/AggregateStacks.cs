using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace LiveStacks
{
    class OneStack : IEquatable<OneStack>
    {
        public ulong[] Addresses { get; }

        public OneStack(ulong[] addresses)
        {
            Addresses = addresses;
        }

        public override int GetHashCode()
        {
            // TODO Can this be sped up? E.g. using SIMD?

            int hc = Addresses.Length;
            for (int i = 0; i < Addresses.Length; ++i)
            {
                hc = unchecked((int)((ulong)hc * 37 + Addresses[i]));
            }
            return hc;
        }

        public override bool Equals(object obj)
        {
            if (!(obj is OneStack))
                return false;

            OneStack other = (OneStack)obj;
            return Equals(other);
        }

        public bool Equals(OneStack other)
        {
            // TODO Can this be sped up? E.g. using SIMD?

            if (Addresses.Length != other.Addresses.Length)
                return false;

            for (int i = 0; i < Addresses.Length; ++i)
                if (Addresses[i] != other.Addresses[i])
                    return false;

            return true;
        }
    }

    class PidStacks
    {
        private ConcurrentDictionary<OneStack, int> CountedStacks { get; } = new ConcurrentDictionary<OneStack, int>();

        public void AddStack(ulong[] addresses)
        {
            var stack = new OneStack(addresses);
            CountedStacks.AddOrUpdate(stack, 1, (_, existingCount) => existingCount + 1);
        }
        
        public List<KeyValuePair<OneStack, int>> TopStacks(int top, int minSamples)
        {
            return CountedStacks.Where(s => s.Value >= minSamples)
                .OrderByDescending(s => s.Value)
                .Take(top).ToList();
        }
    }
}
