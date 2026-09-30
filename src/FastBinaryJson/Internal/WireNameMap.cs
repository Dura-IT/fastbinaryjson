using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace DuraIT.FastBinaryJson.Internal
{
    /// <summary>
    /// Resolves a key exactly as it appears on the wire to the member it names, for one type.
    /// </summary>
    /// <remarks>
    /// The reader used to lowercase every key of every object before the member lookup - a new
    /// string per property per object. The answer depends only on the key, so it is computed once per
    /// distinct key, misses included, and the lookup itself is unchanged: lowercase, then
    /// <see cref="Reflection.Getproperties"/>.
    ///
    /// Keys come from the payload, so whoever wrote the stream chooses them. The cache is capped;
    /// past the cap a key is resolved without being stored, which is the old cost, never unbounded
    /// growth.
    /// </remarks>
    internal sealed class WireNameMap
    {
        private readonly Dictionary<string, myPropInfo> _members;
        private readonly ConcurrentDictionary<string, myPropInfo?> _byWireName = new ConcurrentDictionary<string, myPropInfo?>(StringComparer.Ordinal);
        private readonly int _capacity;
        private int _count;

        public WireNameMap(Dictionary<string, myPropInfo> members)
        {
            _members = members;
            // Room for every member under a few spellings; a legitimate stream never needs more.
            _capacity = Math.Max(32, members.Count * 4);
        }

        public myPropInfo? Find(string wireName)
        {
            if (_byWireName.TryGetValue(wireName, out myPropInfo? found))
                return found;

            _members.TryGetValue(wireName.ToLowerInvariant(), out found);
            if (Volatile.Read(ref _count) < _capacity && _byWireName.TryAdd(wireName, found))
                Interlocked.Increment(ref _count);

            return found;
        }
    }
}
