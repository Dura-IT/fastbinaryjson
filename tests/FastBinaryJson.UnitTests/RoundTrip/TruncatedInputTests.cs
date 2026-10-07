using System;
using System.Linq;
using AwesomeAssertions;
using DuraIT.FastBinaryJson;
using NUnit.Framework;

namespace FastBinaryJson.UnitTests.RoundTrip
{
    /*
     * Input whose lengths run past the end of the payload - truncated in transit, or crafted.
     *
     * Upstream read these through raw pointers with no bounds check: UnicodeGetString built the
     * string straight from memory past the array, and Helper.ToInt16/32/64 checked only the first
     * byte before reading two, four or eight. The result was adjacent heap memory returned as data,
     * or a crash - never an error. For a deserializer that may be handed untrusted bytes, that is a
     * memory-safety defect, not a robustness nicety.
     *
     * Every case must fail with BjsonException, whose InnerException is the ArgumentOutOfRangeException
     * the bounds-checked reads throw.
     */
    [TestFixture]
    [TestOf(typeof(Bjson))]
    public sealed class TruncatedInputTests
    {
        [Test]
        public void Parse_Utf16StringLongerThanThePayload_Throws()
        {
            byte[] bytes = Payload(Tokens.Utf16String, BitConverter.GetBytes(1000), new byte[] { 0x41, 0x00, 0x42, 0x00 });

            FluentActions.Invoking(() => Bjson.Parse(bytes)).Should().Throw<BjsonException>().WithInnerException<ArgumentOutOfRangeException>();
        }

        [Test]
        public void Parse_Utf8StringLongerThanThePayload_Throws()
        {
            byte[] bytes = Payload(Tokens.Utf8String, BitConverter.GetBytes(1000), new byte[] { 0x41, 0x42 });

            FluentActions.Invoking(() => Bjson.Parse(bytes)).Should().Throw<BjsonException>().WithInnerException<ArgumentOutOfRangeException>();
        }

        [TestCase(Tokens.Int16, 1)]
        [TestCase(Tokens.UInt16, 1)]
        [TestCase(Tokens.Char, 1)]
        [TestCase(Tokens.Int32, 2)]
        [TestCase(Tokens.UInt32, 3)]
        [TestCase(Tokens.Int64, 5)]
        [TestCase(Tokens.UInt64, 7)]
        [TestCase(Tokens.DateTime, 4)]
        [TestCase(Tokens.TimeSpan, 6)]
        [TestCase(Tokens.Decimal, 14)]
        [TestCase(Tokens.DateTimeOffset, 9)]
        public void Parse_FixedSizeValueCutShort_Throws(byte token, int bytesPresent)
        {
            byte[] bytes = Payload(token, Enumerable.Repeat((byte)0x7F, bytesPresent).ToArray());

            FluentActions.Invoking(() => Bjson.Parse(bytes)).Should().Throw<BjsonException>().WithInnerException<ArgumentOutOfRangeException>();
        }

        [Test]
        public void Parse_ByteArrayLongerThanThePayload_ThrowsWithoutAllocatingIt()
        {
            byte[] bytes = Payload(Tokens.ByteArray, BitConverter.GetBytes(int.MaxValue), new byte[] { 1, 2, 3 });

            FluentActions.Invoking(() => Bjson.Parse(bytes)).Should().Throw<BjsonException>().WithInnerException<ArgumentOutOfRangeException>();
        }

        [Test]
        public void Parse_Utf16NameLongerThanThePayload_Throws()
        {
            byte[] bytes = Payload(Tokens.DocStart, new[] { Tokens.NameUtf16, (byte)200 }, new byte[] { 0x41, 0x00 });

            FluentActions.Invoking(() => Bjson.Parse(bytes)).Should().Throw<BjsonException>().WithInnerException<ArgumentOutOfRangeException>();
        }

        [Test]
        public void Parse_LongTypedArrayNameLongerThanThePayload_Throws()
        {
            byte[] bytes = Payload(Tokens.TypedArrayLong, BitConverter.GetBytes((short)3000), new byte[] { 0x41, 0x00 });

            FluentActions.Invoking(() => Bjson.Parse(bytes)).Should().Throw<BjsonException>().WithInnerException<ArgumentOutOfRangeException>();
        }

        private static byte[] Payload(byte token, params byte[][] parts)
        {
            return new[] { token }.Concat(parts.SelectMany(p => p)).ToArray();
        }
    }
}
