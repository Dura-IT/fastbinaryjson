using System;
using System.Collections.Generic;

namespace DuraIT.FastBinaryJson.Internal
{
    internal sealed class SafeDictionary<TKey, TValue>
        where TKey : notnull
    {
        private readonly object _Padlock = new object();
        private readonly Dictionary<TKey, TValue> _Dictionary;

        public SafeDictionary(int capacity)
        {
            _Dictionary = new Dictionary<TKey, TValue>(capacity);
        }

        public SafeDictionary()
        {
            _Dictionary = new Dictionary<TKey, TValue>();
        }

        public bool TryGetValue(TKey key, out TValue? value)
        {
            lock (_Padlock)
            {
                if (_Dictionary.TryGetValue(key, out TValue? found))
                {
                    value = found;
                    return true;
                }

                value = default;
                return false;
            }
        }

        public int Count()
        {
            lock (_Padlock) return _Dictionary.Count;
        }

        public TValue this[TKey key]
        {
            get
            {
                lock (_Padlock)
                    return _Dictionary[key];
            }
            set
            {
                lock (_Padlock)
                    _Dictionary[key] = value;
            }
        }

        public void Add(TKey key, TValue value)
        {
            lock (_Padlock)
            {
                if (_Dictionary.ContainsKey(key) == false)
                    _Dictionary.Add(key, value);
            }
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
            fixed (byte* numRef = &(value[startIndex]))
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
            fixed (byte* numRef = &(value[startIndex]))
            {
                return *(((long*)numRef));
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
            fixed (byte* numRef = &(value[startIndex]))
            {
                return *(((short*)numRef));
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
