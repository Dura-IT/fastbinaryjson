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
        public void WriteInt32At_WrittenRange_OverwritesInPlace()
        {
            using PooledByteBuffer buffer = new PooledByteBuffer();
            buffer.Write(new byte[] { 1, 0, 0, 0, 0, 2 });

            buffer.WriteInt32At(1, 0x04030201);

            buffer.ToArray().Should().Equal(new byte[] { 1 }.Concat(BitConverter.GetBytes(0x04030201)).Concat(new byte[] { 2 }));
        }

        [TestCase(3)]
        [TestCase(-1)]
        public void WriteInt32At_OutsideWrittenRange_Throws(int position)
        {
            using PooledByteBuffer buffer = new PooledByteBuffer();
            buffer.Write(new byte[6]);

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
