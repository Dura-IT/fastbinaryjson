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
            // Wire names are matched whatever case they were written in.
            _members = members.Comparer.Equals("a", "A") ? members : new Dictionary<string, myPropInfo>(members, StringComparer.OrdinalIgnoreCase);
            // Room for every member under a few spellings; a legitimate stream never needs more.
            _capacity = Math.Max(32, members.Count * 4);
        }

        public myPropInfo? Find(string wireName)
        {
            if (_byWireName.TryGetValue(wireName, out myPropInfo? found))
                return found;

            _members.TryGetValue(wireName, out found);
            if (Volatile.Read(ref _count) < _capacity && _byWireName.TryAdd(wireName, found))
                Interlocked.Increment(ref _count);

            return found;
        }

        /*
         * Keys exactly as written - name token, length, encoded name - in the order they were first
         * read, so a reader can recognise a key by comparing bytes instead of decoding and hashing its
         * name. Append-only and copy-on-write: a reader takes one snapshot per object, and a concurrent
         * append only means the newer snapshot has one more key. A lost append race drops that key
         * until it is next missed. Bounded like the name cache, for the same reason.
         */
        private WireKey[] _keys = Array.Empty<WireKey>();

        /// <summary>
        /// The keys remembered so far, in first-read order.
        /// </summary>
        public WireKey[] Keys => Volatile.Read(ref _keys);

        /// <summary>
        /// Remembers a key's bytes with what its name resolved to. Ignored past the cap.
        /// </summary>
        public void Remember(byte[] raw, myPropInfo? member, bool special)
        {
            WireKey[] current = Volatile.Read(ref _keys);
            if (current.Length >= _capacity)
                return;

            WireKey[] next = new WireKey[current.Length + 1];
            Array.Copy(current, next, current.Length);
            next[current.Length] = new WireKey(raw, member, special);
            Interlocked.CompareExchange(ref _keys, next, current);
        }
    }

    /// <summary>
    /// One key as written, and what its decoded name resolves to in its <see cref="WireNameMap"/>.
    /// </summary>
    internal sealed class WireKey
    {
        public WireKey(byte[] raw, myPropInfo? member, bool special)
        {
            Raw = raw;
            Member = member;
            Special = special;
        }

        /// <summary>
        /// Token, length and encoded name. Equal bytes are the same name, so they resolve the same.
        /// </summary>
        public byte[] Raw { get; }

        public myPropInfo? Member { get; }

        /// <summary>
        /// The name starts with '$' - $type, $i, $schema - which the reader never treats as a member.
        /// </summary>
        public bool Special { get; }
    }
}
