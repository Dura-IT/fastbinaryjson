#if NET10_0_OR_GREATER
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace DuraIT.FastBinaryJson.Internal
{
    /// <summary>
    /// Resolves a $type value straight from its chars, so reading one no longer allocates its name.
    /// </summary>
    /// <remarks>
    /// The writer turns global types off whenever the root is a List or Dictionary, so every element
    /// of such a root carries its full assembly-qualified type name - around 250 bytes as a string,
    /// allocated once per element only to be looked up and dropped.
    ///
    /// The answer is the one <see cref="Deserializer.ResolveType"/> gives: a $types entry first, then
    /// <see cref="Reflection.GetTypeFromCache"/>. Only that second step is cached here, keyed by the
    /// exact name it was asked for, and only after it returned - a denylisted name throws before it is
    /// added, so it throws again every time. Bounded like <see cref="NameCache"/>, because the names
    /// come from the stream; past the bound a name is resolved through the allocating path as before.
    /// </remarks>
    internal static class TypeNameCache
    {
        private const int MaxTypes = 1024;

        private static readonly ConcurrentDictionary<string, Type?> Types = new ConcurrentDictionary<string, Type?>(StringComparer.Ordinal);
        private static readonly ConcurrentDictionary<string, Type?>.AlternateLookup<ReadOnlySpan<char>> ByChars = Types.GetAlternateLookup<
            ReadOnlySpan<char>
        >();
        private static int _count;

        /// <summary>
        /// The type <see cref="Deserializer.ResolveType"/> would return for this name.
        /// </summary>
        /// <exception cref="InvalidCastException">If a $types entry for the name is not a string.</exception>
        /// <exception cref="Exception">If the name is on the $type denylist.</exception>
        public static Type? Resolve(ReadOnlySpan<char> name, Dictionary<string, object>? globaltypes, Deserializer deserializer)
        {
            if (globaltypes != null && globaltypes.Count > 0)
            {
                if (globaltypes.TryGetAlternateLookup(out Dictionary<string, object>.AlternateLookup<ReadOnlySpan<char>> lookup) == false)
                    return deserializer.ResolveType(new string(name), globaltypes);
                if (lookup.TryGetValue(name, out object? mapped))
                    return deserializer.ResolveGlobalType(mapped);
            }

            if (ByChars.TryGetValue(name, out Type? cached))
                return cached;

            string key = new string(name);
            Type? resolved = Reflection.Instance.GetTypeFromCache(key, true);
            if (Volatile.Read(ref _count) < MaxTypes && Types.TryAdd(key, resolved))
                Interlocked.Increment(ref _count);

            return resolved;
        }
    }
}
#endif
