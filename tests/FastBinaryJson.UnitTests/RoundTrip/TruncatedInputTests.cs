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
     * Every case must fail with ArgumentOutOfRangeException, which is what the bounds-checked UTF-8
     * path has always thrown.
     */
    [TestFixture]
    [TestOf(typeof(BJSON))]
    public sealed class TruncatedInputTests
    {
        [Test]
        public void Parse_Utf16StringLongerThanThePayload_Throws()
        {
            byte[] bytes = Payload(TOKENS.UNICODE_STRING, BitConverter.GetBytes(1000), new byte[] { 0x41, 0x00, 0x42, 0x00 });

            FluentActions.Invoking(() => BJSON.Parse(bytes)).Should().Throw<ArgumentOutOfRangeException>();
        }

        [Test]
        public void Parse_Utf8StringLongerThanThePayload_Throws()
        {
            byte[] bytes = Payload(TOKENS.STRING, BitConverter.GetBytes(1000), new byte[] { 0x41, 0x42 });

            FluentActions.Invoking(() => BJSON.Parse(bytes)).Should().Throw<ArgumentOutOfRangeException>();
        }

        [TestCase(TOKENS.SHORT, 1)]
        [TestCase(TOKENS.USHORT, 1)]
        [TestCase(TOKENS.CHAR, 1)]
        [TestCase(TOKENS.INT, 2)]
        [TestCase(TOKENS.UINT, 3)]
        [TestCase(TOKENS.LONG, 5)]
        [TestCase(TOKENS.ULONG, 7)]
        [TestCase(TOKENS.DATETIME, 4)]
        [TestCase(TOKENS.TIMESPAN, 6)]
        [TestCase(TOKENS.DECIMAL, 14)]
        [TestCase(TOKENS.DATETIMEOFFSET, 9)]
        public void Parse_FixedSizeValueCutShort_Throws(byte token, int bytesPresent)
        {
            byte[] bytes = Payload(token, Enumerable.Repeat((byte)0x7F, bytesPresent).ToArray());

            FluentActions.Invoking(() => BJSON.Parse(bytes)).Should().Throw<ArgumentOutOfRangeException>();
        }

        [Test]
        public void Parse_ByteArrayLongerThanThePayload_ThrowsWithoutAllocatingIt()
        {
            byte[] bytes = Payload(TOKENS.BYTEARRAY, BitConverter.GetBytes(int.MaxValue), new byte[] { 1, 2, 3 });

            FluentActions.Invoking(() => BJSON.Parse(bytes)).Should().Throw<ArgumentOutOfRangeException>();
        }

        [Test]
        public void Parse_Utf16NameLongerThanThePayload_Throws()
        {
            byte[] bytes = Payload(TOKENS.DOC_START, new[] { TOKENS.NAME_UNI, (byte)200 }, new byte[] { 0x41, 0x00 });

            FluentActions.Invoking(() => BJSON.Parse(bytes)).Should().Throw<ArgumentOutOfRangeException>();
        }

        [Test]
        public void Parse_LongTypedArrayNameLongerThanThePayload_Throws()
        {
            byte[] bytes = Payload(TOKENS.ARRAY_TYPED_LONG, BitConverter.GetBytes((short)3000), new byte[] { 0x41, 0x00 });

            FluentActions.Invoking(() => BJSON.Parse(bytes)).Should().Throw<ArgumentOutOfRangeException>();
        }

        private static byte[] Payload(byte token, params byte[][] parts)
        {
            return new[] { token }.Concat(parts.SelectMany(p => p)).ToArray();
        }
    }
}
