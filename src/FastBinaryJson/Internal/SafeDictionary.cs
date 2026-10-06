using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace DuraIT.FastBinaryJson.Internal
{
    /// <summary>
    /// The thread-safe cache behind every reflection lookup, read on every object written or read.
    /// </summary>
    /// <remarks>
    /// Upstream wrapped a Dictionary in a lock, so each of those reads took and released a monitor -
    /// several per object, for caches written once per type and read thousands of times per call.
    /// ConcurrentDictionary reads without locking. Its own Count and IsEmpty take every lock when the
    /// dictionary is empty, which is exactly the common case for the custom-type registry checked per
    /// object, so the count is kept here instead. Add keeps upstream's add-if-absent semantics.
    /// </remarks>
    internal sealed class SafeDictionary<TKey, TValue>
        where TKey : notnull
    {
        private readonly ConcurrentDictionary<TKey, TValue> _dictionary;
        private int _count;

        public SafeDictionary(int capacity)
        {
            _dictionary = new ConcurrentDictionary<TKey, TValue>(Environment.ProcessorCount, capacity);
        }

        public SafeDictionary()
        {
            _dictionary = new ConcurrentDictionary<TKey, TValue>();
        }

        public bool TryGetValue(TKey key, out TValue? value)
        {
            if (_dictionary.TryGetValue(key, out TValue? found))
            {
                value = found;
                return true;
            }

            value = default;
            return false;
        }

        public int Count() => Volatile.Read(ref _count);

        /// <summary>
        /// Gets the value stored for a key, or stores one.
        /// </summary>
        /// <param name="key">The key.</param>
        /// <exception cref="KeyNotFoundException">On get, if the key is absent.</exception>
        public TValue this[TKey key]
        {
            get => _dictionary[key];
            set
            {
                if (_dictionary.TryAdd(key, value))
                    Interlocked.Increment(ref _count);
                else
                    _dictionary[key] = value;
            }
        }

        public void Add(TKey key, TValue value)
        {
            if (_dictionary.TryAdd(key, value))
                Interlocked.Increment(ref _count);
        }
    }

    internal static class Helper
    {
        internal static int ToInt32(byte[] value, int startIndex, bool reverse)
        {
            if (reverse)
            {
                byte[] b = new byte[sizeof(int)];
                Buffer.BlockCopy(value, startIndex, b, 0, sizeof(int));
                Array.Reverse(b);
                return ToInt32(b, 0);
            }

            return ToInt32(value, startIndex);
        }

        internal static int ToInt32(byte[] value, int startIndex)
        {
            CheckRange(value, startIndex, sizeof(int));
            return BitConverter.ToInt32(value, startIndex);
        }

        internal static long ToInt64(byte[] value, int startIndex, bool reverse)
        {
            if (reverse)
            {
                byte[] b = new byte[sizeof(long)];
                Buffer.BlockCopy(value, startIndex, b, 0, sizeof(long));
                Array.Reverse(b);
                return ToInt64(b, 0);
            }
            return ToInt64(value, startIndex);
        }

        internal static long ToInt64(byte[] value, int startIndex)
        {
            CheckRange(value, startIndex, sizeof(long));
            return BitConverter.ToInt64(value, startIndex);
        }

        internal static short ToInt16(byte[] value, int startIndex, bool reverse)
        {
            if (reverse)
            {
                byte[] b = new byte[sizeof(short)];
                Buffer.BlockCopy(value, startIndex, b, 0, sizeof(short));
                Array.Reverse(b);
                return ToInt16(b, 0);
            }
            return ToInt16(value, startIndex);
        }

        internal static short ToInt16(byte[] value, int startIndex)
        {
            CheckRange(value, startIndex, sizeof(short));
            return BitConverter.ToInt16(value, startIndex);
        }

        /*
         * BitConverter reports a short read as ArgumentException, not ArgumentOutOfRangeException, and
         * the callers and the truncated-input tests rely on the latter, so the range is checked here first.
         */
        private static void CheckRange(byte[] value, int startIndex, int size)
        {
            if (startIndex < 0 || startIndex > value.Length - size)
                throw new ArgumentOutOfRangeException(nameof(startIndex));
        }

        /// <summary>
        /// Throws when a length read from the payload runs past the end of it.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">If <paramref name="length"/> is negative or runs past the payload.</exception>
        internal static void CheckLength(byte[] json, int start, int length, string what)
        {
            if (length < 0 || start > json.Length - length)
                throw new ArgumentOutOfRangeException(nameof(length), what + " length runs past the end of the payload.");
        }

        internal static byte[] GetBytes(long num, bool reverse)
        {
            byte[] buffer = BitConverter.GetBytes(num);
            if (reverse)
                Array.Reverse(buffer);
            return buffer;
        }

        public static byte[] GetBytes(int num, bool reverse)
        {
            byte[] buffer = BitConverter.GetBytes(num);
            if (reverse)
                Array.Reverse(buffer);
            return buffer;
        }
    }
}
