using System;
using DuraIT.FastBinaryJson.Internal;

namespace FastBinaryJson.UnitTests
{
    internal static class AllocationProbe
    {
        /// <summary>
        /// Bytes <paramref name="action" /> allocates per call on this thread, after a warm-up that gets the JIT and the
        /// per-type caches out of the number.
        /// </summary>
        internal static long PerCall(Action action)
        {
            Guard.NotNull(action, nameof(action));

            for (int i = 0; i < 20; i++)
                action();

            long before = GC.GetAllocatedBytesForCurrentThread();
            const int runs = 50;
            for (int i = 0; i < runs; i++)
                action();
            return (GC.GetAllocatedBytesForCurrentThread() - before) / runs;
        }
    }
}
