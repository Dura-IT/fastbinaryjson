using System;
using System.Linq;
using AwesomeAssertions;
using DuraIT.FastBinaryJson.Internal;
using NUnit.Framework;

namespace FastBinaryJson.UnitTests.Internal
{
    /*
     * net10.0 only: the netstandard2.0 test project removes this file from its compile glob, because
     * the netstandard2.0 build of the library has no PooledByteBuffer.
     */
    [TestFixture]
    [TestOf(typeof(PooledByteBuffer))]
    public sealed class PooledByteBufferTests
    {
        // Every buffer starts at the length the previous one reached; the growth tests need a small start.
        [SetUp]
        public void SetUp()
        {
            PooledByteBuffer.ResetSizeHint();
        }

        [Test]
        public void Constructor_AfterALargerBuffer_StartsWithRoomForItsLength()
        {
            using (PooledByteBuffer first = new PooledByteBuffer())
                first.Write(new byte[10_000]);

            using PooledByteBuffer next = new PooledByteBuffer();

            next.Capacity.Should().BeGreaterThanOrEqualTo(10_000);
        }

        [Test]
        public void Constructor_AfterAHugeBuffer_StartsAtTheCap()
        {
            using (PooledByteBuffer first = new PooledByteBuffer())
                first.Write(new byte[(1 << 21) + 1]);

            using PooledByteBuffer next = new PooledByteBuffer();

            next.Capacity.Should().Be(1 << 20);
        }

        [Test]
        public void Write_BeyondInitialCapacity_KeepsEveryByte()
        {
            byte[] expected = Enumerable.Range(0, 5000).Select(i => (byte)(i * 7)).ToArray();

            using PooledByteBuffer buffer = new PooledByteBuffer();
            buffer.WriteByte(expected[0]);
            buffer.Write(expected.AsSpan(1, 300));
            foreach (byte b in expected.Skip(301).Take(1000))
                buffer.WriteByte(b);
            buffer.Write(expected.AsSpan(1301));

            buffer.Length.Should().Be(expected.Length);
            buffer.ToArray().Should().Equal(expected);
        }

        [Test]
        public void GetSpan_PastCapacityThenAdvance_KeepsEarlierAndCommittedBytes()
        {
            byte[] expected = Enumerable.Range(0, 1003).Select(i => (byte)(i * 7)).ToArray();

            using PooledByteBuffer buffer = new PooledByteBuffer();
            buffer.Write(expected.AsSpan(0, 3));
            Span<byte> free = buffer.GetSpan(1000);
            expected.AsSpan(3).CopyTo(free);
            buffer.Advance(1000);

            free.Length.Should().BeGreaterThanOrEqualTo(1000);
            buffer.ToArray().Should().Equal(expected);
        }

        [Test]
        public void Advance_Zero_LeavesLengthUnchanged()
        {
            using PooledByteBuffer buffer = new PooledByteBuffer();
            buffer.Write(new byte[5]);
            buffer.GetSpan(0);

            buffer.Advance(0);

            buffer.Length.Should().Be(5);
        }

        [TestCase(-1)]
        [TestCase(1)]
        public void Advance_OutsideFreeSpace_Throws(int pastFreeSpace)
        {
            using PooledByteBuffer buffer = new PooledByteBuffer();
            int free = buffer.GetSpan(10).Length;
            int count = pastFreeSpace < 0 ? pastFreeSpace : free + pastFreeSpace;

            // ReSharper disable once AccessToDisposedClosure - the buffer is disposed at the end of the test, after the call
            FluentActions.Invoking(() => buffer.Advance(count)).Should().Throw<ArgumentOutOfRangeException>();
            buffer.Length.Should().Be(0);
        }

        [Test]
        public void WriteByte_AfterDispose_ThrowsObjectDisposed()
        {
            PooledByteBuffer buffer = new PooledByteBuffer();
            buffer.Dispose();

            FluentActions.Invoking(() => buffer.WriteByte(1)).Should().Throw<ObjectDisposedException>();
        }

        [Test]
        public void Write_AfterDispose_ThrowsObjectDisposed()
        {
            PooledByteBuffer buffer = new PooledByteBuffer();
            buffer.Dispose();

            FluentActions.Invoking(() => buffer.Write(new byte[] { 1, 2 })).Should().Throw<ObjectDisposedException>();
        }

        /// <summary>
        /// A write that exactly fills the buffer stays on the fast path; the next byte must grow it.
        /// </summary>
        [Test]
        public void Write_ExactlyFillingThenOneMore_KeepsEveryByte()
        {
            using PooledByteBuffer buffer = new PooledByteBuffer();
            byte[] fill = Enumerable.Range(0, buffer.GetSpan(1).Length).Select(i => (byte)i).ToArray();

            buffer.Write(fill);
            buffer.WriteByte(255);

            buffer.ToArray().Should().Equal(fill.Concat(new byte[] { 255 }));
        }

        [Test]
        public void GetSpan_AfterDispose_ThrowsObjectDisposed()
        {
            PooledByteBuffer buffer = new PooledByteBuffer();
            buffer.Dispose();

            FluentActions.Invoking(() => buffer.GetSpan(1)).Should().Throw<ObjectDisposedException>();
        }

        [Test]
        public void WriteInt32At_WrittenRange_OverwritesInPlace()
        {
            using PooledByteBuffer buffer = new PooledByteBuffer();
            buffer.Write(new byte[] { 1, 0, 0, 0, 0, 2 });

            buffer.WriteInt32At(1, 0x04030201);

            buffer
                .ToArray()
                .Should()
                .Equal(
                    new byte[] { 1 }
                        .Concat(BitConverter.GetBytes(0x04030201))
                        .Concat(new byte[] { 2 })
                );
        }

        [TestCase(3)]
        [TestCase(-1)]
        public void WriteInt32At_OutsideWrittenRange_Throws(int position)
        {
            using PooledByteBuffer buffer = new PooledByteBuffer();
            buffer.Write(new byte[6]);

            // ReSharper disable once AccessToDisposedClosure - the buffer is disposed at the end of the test, after the call
            FluentActions.Invoking(() => buffer.WriteInt32At(position, 1)).Should().Throw<ArgumentOutOfRangeException>();
        }

        [Test]
        public void Dispose_Twice_DoesNotThrow()
        {
            PooledByteBuffer buffer = new PooledByteBuffer();
            buffer.Dispose();

            FluentActions.Invoking(buffer.Dispose).Should().NotThrow();
        }

        [Test]
        public void ToArray_AfterDispose_ThrowsObjectDisposed()
        {
            PooledByteBuffer buffer = new PooledByteBuffer();
            buffer.Dispose();

            FluentActions.Invoking(buffer.ToArray).Should().Throw<ObjectDisposedException>();
        }
    }
}
