using AwesomeAssertions;

using DuraIT.FastBinaryJson;

using NUnit.Framework;

namespace FastBinaryJson.UnitTests.RoundTrip
{
    /*
     * Round-trip tests for the primitives upstream could not restore.
     *
     * These replace characterization tests that asserted the broken behaviour; KnownDefectTests
     * says why a red test there is good news and what to do about it.
     *
     * The typed and the untyped read path are asserted separately on purpose. A typed property can
     * be repaired by converting at assignment time, so a green typed test says nothing about what
     * BJSON.Parse hands back to a caller reading an untyped graph - which is the path where a wrong
     * CLR type is silent rather than an exception.
     */
    [TestFixture]
    [TestOf(typeof(BJSON))]
    public sealed class PrimitiveRoundTripTests
    {
        [TestCase('Z')]
        [TestCase('\0')]
        [TestCase('é')]
        [TestCase(char.MaxValue)]
        public void Char_TypedProperty_RoundTrips(char value)
        {
            byte[] bytes = BJSON.ToBJSON(new CharHolder { Value = value });

            CharHolder restored = BJSON.ToObject<CharHolder>(bytes)!;

            restored.Value.Should().Be(value);
        }

        [Test]
        public void Char_Untyped_RoundTripsAsChar()
        {
            byte[] bytes = BJSON.ToBJSON('Z');

            object? parsed = BJSON.Parse(bytes);

            parsed.Should().BeOfType<char>("ParseChar casts the Int16 it reads back to char");
            parsed.Should().Be('Z');
        }

        /// <summary>
        /// Pins that the fix is on the read side only.
        /// </summary>
        /// <remarks>
        /// WriteChar was always correct - it writes TOKENS.CHAR and the value as Int16 - so the
        /// bytes for a char must not have moved. The golden fixtures cover the same ground for the
        /// corpus types; this states it for the primitive on its own, where it is the whole point.
        /// </remarks>
        [Test]
        public void Char_Bytes_AreTokenAndInt16()
        {
            byte[] bytes = BJSON.ToBJSON('Z');

            bytes.Should().Equal(TOKENS.CHAR, 0x5A, 0x00);
        }

        private sealed class CharHolder
        {
            public char Value { get; set; }
        }
    }
}
