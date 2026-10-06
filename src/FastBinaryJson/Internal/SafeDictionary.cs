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
        private readonly ConcurrentDictionary<TKey, TValue> _Dictionary;
        private int _count;

        public SafeDictionary(int capacity)
        {
            _Dictionary = new ConcurrentDictionary<TKey, TValue>(Environment.ProcessorCount, capacity);
        }

        public SafeDictionary()
        {
            _Dictionary = new ConcurrentDictionary<TKey, TValue>();
        }

        public bool TryGetValue(TKey key, out TValue? value)
        {
            if (_Dictionary.TryGetValue(key, out TValue? found))
            {
                value = found;
                return true;
            }

            value = default;
            return false;
        }

        public int Count() => Volatile.Read(ref _count);

        /// <exception cref="KeyNotFoundException">On get, if the key is absent.</exception>
        public TValue this[TKey key]
        {
            get => _Dictionary[key];
            set
            {
                if (_Dictionary.TryAdd(key, value))
                    Interlocked.Increment(ref _count);
                else
                    _Dictionary[key] = value;
            }
        }

        public void Add(TKey key, TValue value)
        {
            if (_Dictionary.TryAdd(key, value))
                Interlocked.Increment(ref _count);
        }
    }

    internal static class Helper
    {
        internal static unsafe int ToInt32(byte[] value, int startIndex, bool reverse)
        {
            if (reverse)
            {
                byte[] b = new byte[4];
                Buffer.BlockCopy(value, startIndex, b, 0, 4);
                Array.Reverse(b);
                return ToInt32(b, 0);
            }

            return ToInt32(value, startIndex);
        }

        internal static unsafe int ToInt32(byte[] value, int startIndex)
        {
            CheckRange(value, startIndex, sizeof(int));
            fixed (byte* numRef = &value[startIndex])
            {
                return *((int*)numRef);
            }
        }

        internal static unsafe long ToInt64(byte[] value, int startIndex, bool reverse)
        {
            if (reverse)
            {
                byte[] b = new byte[8];
                Buffer.BlockCopy(value, startIndex, b, 0, 8);
                Array.Reverse(b);
                return ToInt64(b, 0);
            }
            return ToInt64(value, startIndex);
        }

        internal static unsafe long ToInt64(byte[] value, int startIndex)
        {
            CheckRange(value, startIndex, sizeof(long));
            fixed (byte* numRef = &value[startIndex])
            {
                return *(long*)numRef;
            }
        }

        internal static unsafe short ToInt16(byte[] value, int startIndex, bool reverse)
        {
            if (reverse)
            {
                byte[] b = new byte[2];
                Buffer.BlockCopy(value, startIndex, b, 0, 2);
                Array.Reverse(b);
                return ToInt16(b, 0);
            }
            return ToInt16(value, startIndex);
        }

        internal static unsafe short ToInt16(byte[] value, int startIndex)
        {
            CheckRange(value, startIndex, sizeof(short));
            fixed (byte* numRef = &value[startIndex])
            {
                return *(short*)numRef;
            }
        }

        /*
         * The pointer reads above cover `size` bytes, but taking &value[startIndex] only checks the
         * first. Without this, a value cut short at the end of the payload read past the array and
         * returned whatever memory followed as data.
         */
        private static void CheckRange(byte[] value, int startIndex, int size)
        {
            if (startIndex < 0 || startIndex > value.Length - size)
                throw new ArgumentOutOfRangeException(nameof(startIndex));
        }

        internal static unsafe byte[] GetBytes(long num, bool reverse)
        {
            byte[] buffer = new byte[8];
            fixed (byte* numRef = buffer)
            {
                *((long*)numRef) = num;
            }
            if (reverse)
                Array.Reverse(buffer);
            return buffer;
        }

        public static unsafe byte[] GetBytes(int num, bool reverse)
        {
            byte[] buffer = new byte[4];
            fixed (byte* numRef = buffer)
            {
                *((int*)numRef) = num;
            }
            if (reverse)
                Array.Reverse(buffer);
            return buffer;
        }
    }
}
