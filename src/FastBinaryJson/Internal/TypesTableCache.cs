using System;
using System.Collections.Generic;

namespace DuraIT.FastBinaryJson.Internal
{
    /*
     * The $types table is the same bytes in every payload a writer produces for the same type graph, and
     * parsing it - two dictionaries and the type-name strings - was most of what a small typed read
     * allocated. The last table seen on this thread is kept with the exact bytes it was parsed from, and
     * reused when the next payload carries the same bytes: equal input parses to an equal table, so a hit
     * can never return a wrong table. The kept dictionary is a master that is never handed out - the
     * caller copies it, because a nested object's own table is merged into whatever dictionary is in effect.
     * The table sits at the end of the payload, so the bytes compared run from the pointer to the end;
     * a payload whose table is followed by a lot of other data simply never hits.
     */
    internal static class TypesTableCache
    {
        private const int MaxCachedTableBytes = 4096;

        [ThreadStatic]
        private static byte[]? _bytes;

        [ThreadStatic]
        private static Dictionary<string, object>? _types;

        /// <summary>
        /// Looks up the table that <paramref name="pointer"/> leads to in <paramref name="json"/>.
        /// </summary>
        /// <param name="json">The payload.</param>
        /// <param name="pointer">The offset of the table, as read from the $types pointer.</param>
        /// <param name="types">The cached master table, which must be copied and never modified.</param>
        /// <returns>True when the bytes from the pointer to the end equal the cached table's bytes.</returns>
        internal static bool TryGet(byte[] json, int pointer, out Dictionary<string, object>? types)
        {
            types = null;
            byte[]? bytes = _bytes;
            Dictionary<string, object>? cached = _types;
            if (bytes == null || cached == null || pointer <= 0 || pointer >= json.Length || json.Length - pointer != bytes.Length)
                return false;

            if (!new ReadOnlySpan<byte>(json, pointer, bytes.Length).SequenceEqual(bytes))
                return false;

            types = cached;
            return true;
        }

        /// <summary>
        /// Keeps the table parsed from <paramref name="pointer"/> in <paramref name="json"/>.
        /// </summary>
        /// <param name="json">The payload.</param>
        /// <param name="pointer">The offset of the table.</param>
        /// <param name="types">The parsed table; the cache takes ownership of it.</param>
        internal static void Remember(byte[] json, int pointer, Dictionary<string, object> types)
        {
            if (pointer <= 0 || pointer >= json.Length || json.Length - pointer > MaxCachedTableBytes)
                return;

            _bytes = new ReadOnlySpan<byte>(json, pointer, json.Length - pointer).ToArray();
            _types = types;
        }
    }
}
