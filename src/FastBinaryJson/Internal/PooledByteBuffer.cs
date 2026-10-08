using System;
using System.Buffers;
using System.Runtime.CompilerServices;
using System.Threading;

namespace DuraIT.FastBinaryJson.Internal
{
    /// <summary>
    /// Growable output buffer backed by <see cref="ArrayPool{T}.Shared"/>, standing in for the
    /// MemoryStream the serializer writes to, on both targets.
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
        private const int MinimumCapacity = 256;
        private const int MaximumHint = 1 << 20;

        /*
         * The length the previous buffer reached, so the next one starts there instead of doubling
         * up from 256 and copying everything written at every step - for a large payload as much
         * copying again as the output itself. Capped, so one huge payload does not make every later
         * call rent a huge array. Shared by all threads: a race only gives a less fitting start size.
         */
        private static int _sizeHint;

        private byte[]? _buffer;
        private int _length;

        public PooledByteBuffer()
        {
            _buffer = ArrayPool<byte>.Shared.Rent(Math.Max(MinimumCapacity, Volatile.Read(ref _sizeHint)));
        }

        public int Length => _length;

        /// <summary>
        /// Forgets the start size, so a test can rely on a buffer starting small and growing.
        /// </summary>
        internal static void ResetSizeHint() => Volatile.Write(ref _sizeHint, 0);

        internal int Capacity => Buffer.Length;

        /*
         * WriteByte and Write run once per token, length and value - thousands of times per call.
         * They used to go straight through EnsureCapacity, which the JIT does not inline (it rents
         * and throws), so every single byte paid a call. The common case - room left - is now
         * inlined and growth moved out to the *Slow methods. Measured 2026-10-01: Serialize -7% to
         * -30% depending on the payload, most on UTF-16 output; this was also the GuidDense UTF-16
         * regression step 1 introduced, since MemoryStream's WriteByte had no such call.
         *
         * A null buffer (disposed) takes the slow path, whose EnsureCapacity throws as before.
         */
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteByte(byte value)
        {
            byte[]? buffer = _buffer;
            int length = _length;
            if (buffer is not null && (uint)length < (uint)buffer.Length)
            {
                buffer[length] = value;
                _length = length + 1;
                return;
            }

            WriteByteSlow(value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Write(ReadOnlySpan<byte> bytes)
        {
            byte[]? buffer = _buffer;
            int length = _length;
            if (buffer is not null && bytes.Length <= buffer.Length - length)
            {
                bytes.CopyTo(new Span<byte>(buffer, length, bytes.Length));
                _length = length + bytes.Length;
                return;
            }

            WriteSlow(bytes);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void WriteByteSlow(byte value)
        {
            byte[] buffer = EnsureCapacity(1);
            buffer[_length++] = value;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void WriteSlow(ReadOnlySpan<byte> bytes)
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

            Unsafe.WriteUnaligned(ref Buffer[position], value);
        }

        public byte[] ToArray() => Buffer.AsSpan(0, _length).ToArray();

        #region IDisposable

        public void Dispose()
        {
            if (_buffer is null)
                return;

            Volatile.Write(ref _sizeHint, Math.Min(_length, MaximumHint));
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
