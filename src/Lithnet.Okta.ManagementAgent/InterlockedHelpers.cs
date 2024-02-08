using System;
using System.Threading;

namespace Lithnet.Okta.ManagementAgent
{
    internal static class InterlockedHelpers
    {
        public static long InterlockedCombine(ref long location, Func<long, long> update)
        {
            long initialValue, newValue;

            do
            {
                initialValue = location;
                newValue = update(initialValue);
            }
            while (Interlocked.CompareExchange(ref location, newValue, initialValue) != initialValue);

            return initialValue;
        }

        public static long InterlockedMax(ref long location, long value)
        {
            return InterlockedHelpers.InterlockedCombine(ref location, v => Math.Max(v, value));
        }
    }
}
