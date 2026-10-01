#if NET10_0_OR_GREATER
using System;
using System.Buffers;
using System.Runtime.InteropServices;

namespace DuraIT.FastBinaryJson.Internal
{
    /// <summary>
    /// Growable output buffer backed by <see cref="ArrayPool{T}.Shared"/>, standing in for the
    /// MemoryStream the serializer writes to on net10.0.
    /// </summary>
    /// <remarks>
    /// MemoryStream doubles by allocating, and every buffer past 85 KB lands on the large object
    /// heap, so a large payload left a trail of discarded LOH arrays and gen2 collections behind it.
    /// Renting instead means only the final <see cref="ToArray"/> copy is allocated. Member names
    /// match MemoryStream's so the serializer's call sites are the same on both targets;
    /// <see cref="GetSpan"/> and <see cref="Advance"/> are the exception, net10.0-only, and named
    /// after <see cref="IBufferWriter{T}"/>.
    /// </remarks>
    internal sealed class PooledByteBuffer : IDisposable
    {
        private const int InitialCapacity = 256;

        private byte[]? _buffer;
        private int _length;

        public PooledByteBuffer()
        {
            _buffer = ArrayPool<byte>.Shared.Rent(InitialCapacity);
        }

        public int Length => _length;

        public void WriteByte(byte value)
        {
            byte[] buffer = EnsureCapacity(1);
            buffer[_length++] = value;
        }

        public void Write(ReadOnlySpan<byte> bytes)
        {
            byte[] buffer = EnsureCapacity(bytes.Length);
            bytes.CopyTo(buffer.AsSpan(_length));
            _length += bytes.Length;
        }

        /// <summary>
        /// Returns the free space after the written bytes, at least <paramref name="sizeHint"/>
        /// long, to fill and then commit with <see cref="Advance"/>.
        /// </summary>
        /// <remarks>
        /// Only valid until the next write: any write may move the data to a larger array.
        /// </remarks>
        public Span<byte> GetSpan(int sizeHint)
        {
            byte[] buffer = EnsureCapacity(sizeHint);
            return buffer.AsSpan(_length);
        }

        /// <summary>
        /// Commits <paramref name="count"/> bytes written into the span from <see cref="GetSpan"/>.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">If count is negative or past the free space.</exception>
        public void Advance(int count)
        {
            if (count < 0 || count > Buffer.Length - _length)
                throw new ArgumentOutOfRangeException(nameof(count));

            _length += count;
        }

        /// <summary>
        /// Overwrites four bytes already written, in native order.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">If the range was not written yet.</exception>
        public void WriteInt32At(int position, int value)
        {
            if (position < 0 || position > _length - sizeof(int))
                throw new ArgumentOutOfRangeException(nameof(position));

            MemoryMarshal.Write(Buffer.AsSpan(position), in value);
        }

        public byte[] ToArray() => Buffer.AsSpan(0, _length).ToArray();

        #region IDisposable

        public void Dispose()
        {
            if (_buffer is null)
                return;

            ArrayPool<byte>.Shared.Return(_buffer);
            _buffer = null;
        }

        #endregion

        private byte[] Buffer => _buffer ?? throw new ObjectDisposedException(nameof(PooledByteBuffer));

        private byte[] EnsureCapacity(int additional)
        {
            byte[] buffer = Buffer;
            int required = _length + additional;
            if (required <= buffer.Length)
                return buffer;

            byte[] grown = ArrayPool<byte>.Shared.Rent(Math.Max(required, buffer.Length * 2));
            buffer.AsSpan(0, _length).CopyTo(grown);
            ArrayPool<byte>.Shared.Return(buffer);
            _buffer = grown;
            return grown;
        }
    }
}
#endif