#if NET10_0_OR_GREATER
using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Threading;

namespace DuraIT.FastBinaryJson.Internal
{
    /// <summary>
    /// Shared intern table for member names read off the wire.
    /// </summary>
    /// <remarks>
    /// Every object in a stream repeats its member names, and the parser used to allocate a new
    /// string for each one. Names are looked up here straight from the payload bytes, so a name that
    /// has been seen before costs no allocation at all.
    ///
    /// Bounded twice over, because names are chosen by whoever wrote the stream: only short names are
    /// interned, and only up to a fixed count. Past either limit the name is allocated as before.
    /// </remarks>
    internal static class NameCache
    {
        private const int MaxNameLength = 64;
        private const int MaxNames = 4096;

        private static readonly ConcurrentDictionary<string, string> Names = new ConcurrentDictionary<string, string>(StringComparer.Ordinal);
        private static readonly ConcurrentDictionary<string, string>.AlternateLookup<ReadOnlySpan<char>> ByChars = Names.GetAlternateLookup<
            ReadOnlySpan<char>
        >();
        private static int _count;

        /// <summary>
        /// A UTF-16 name, read in place: the bytes are the name's chars in native order, exactly
        /// what <see cref="TypeReflector.UnicodeGetString(byte[], int, int)"/> copies into a new string.
        /// </summary>
        public static string FromUtf16(byte[] bytes, int offset, int length)
        {
            ReadOnlySpan<char> chars = MemoryMarshal.Cast<byte, char>(new ReadOnlySpan<byte>(bytes, offset, length));
            return Get(chars);
        }

        /// <summary>
        /// A UTF-8 name, decoded onto the stack with the same encoder as
        /// <see cref="TypeReflector.Utf8GetString"/>.
        /// </summary>
        public static string FromUtf8(byte[] bytes, int offset, int length)
        {
            // A UTF-8 byte never yields more than one char, so this bounds the decoded length.
            if (length > MaxNameLength)
                return TypeReflector.Utf8GetString(bytes, offset, length);

            Span<char> chars = stackalloc char[MaxNameLength];
            int written = TypeReflector.Utf8GetChars(new ReadOnlySpan<byte>(bytes, offset, length), chars);
            return Get(chars.Slice(0, written));
        }

        private static string Get(ReadOnlySpan<char> chars)
        {
            if (ByChars.TryGetValue(chars, out string? name))
                return name;

            name = new string(chars);
            if (chars.Length <= MaxNameLength && Volatile.Read(ref _count) < MaxNames && Names.TryAdd(name, name))
                Interlocked.Increment(ref _count);

            return name;
        }
    }
}
#endif
