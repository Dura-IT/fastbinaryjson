using System;

using AwesomeAssertions;

using DuraIT.FastBinaryJson;

using NUnit.Framework;

namespace FastBinaryJson.UnitTests.RoundTrip
{
    /*
     * Round-trip tests for the primitives upstream could not restore.
     *
     * These replaced characterization tests that asserted the broken behaviour. That file is gone:
     * once every defect it pinned was fixed, there was nothing left for it to hold.
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

        [TestCase((sbyte)0)]
        [TestCase((sbyte)42)]
        [TestCase((sbyte)-42)]
        [TestCase(sbyte.MinValue)]
        [TestCase(sbyte.MaxValue)]
        public void SByte_TypedProperty_RoundTrips(sbyte value)
        {
            byte[] bytes = BJSON.ToBJSON(new SByteHolder { Value = value });

            SByteHolder restored = BJSON.ToObject<SByteHolder>(bytes)!;

            restored.Value.Should().Be(value);
        }

        [Test]
        public void SByte_Untyped_RoundTripsAsSByte()
        {
            byte[] bytes = BJSON.ToBJSON((sbyte)-42);

            object? parsed = BJSON.Parse(bytes);

            parsed.Should().BeOfType<sbyte>("sbyte has its own token, so the reader knows the value is signed");
            parsed.Should().Be((sbyte)-42);
        }

        /// <summary>
        /// Pins the added token, because this one is a wire-format change.
        /// </summary>
        /// <remarks>
        /// sbyte shared TOKENS.BYTE with byte, which made the two indistinguishable on the wire, so
        /// unlike char there was nothing for the reader to recover. TOKENS.SBYTE is what makes the
        /// sign part of the format.
        /// </remarks>
        [Test]
        public void SByte_Bytes_AreDedicatedTokenAndTwosComplement()
        {
            byte[] bytes = BJSON.ToBJSON((sbyte)-42);

            bytes.Should().Equal(TOKENS.SBYTE, 0xD6);
        }

        /// <summary>
        /// A stream written before the token existed still restores into a typed sbyte property.
        /// </summary>
        /// <remarks>
        /// The legacy bytes are built by writing the current form and swapping the token back, so
        /// this cannot drift out of step with a hand-typed literal. UseExtensions is off to keep the
        /// document free of the $type string, which makes the token position unambiguous - and the
        /// byte after it is asserted so the search cannot silently find the wrong one.
        /// </remarks>
        [Test]
        public void SByte_LegacyByteToken_StillRestoresIntoTypedProperty()
        {
            BJSONParameters parameters = new BJSONParameters { UseExtensions = false };
            byte[] legacy = BJSON.ToBJSON(new SByteHolder { Value = -42 }, parameters);

            int token = Array.IndexOf(legacy, TOKENS.SBYTE);
            token.Should().BeGreaterThanOrEqualTo(0);
            legacy[token + 1].Should().Be(0xD6, "the byte after the token must be the value, or the wrong token was found");
            legacy[token] = TOKENS.BYTE;

            SByteHolder restored = BJSON.ToObject<SByteHolder>(legacy, parameters)!;

            restored.Value.Should().Be(-42, "a byte assigned to an sbyte property is reinterpreted, which recovers the sign");
        }

        /// <summary>
        /// The limit of the legacy path, stated so it is not mistaken for an oversight.
        /// </summary>
        /// <remarks>
        /// Reading an untyped graph out of a legacy stream still yields a byte. There is no declared
        /// property type to convert against and the old bytes do not record the sign, so nothing can
        /// recover it. Only data written with TOKENS.SBYTE round-trips untyped.
        /// </remarks>
        [Test]
        public void SByte_LegacyByteToken_Untyped_IsStillAByte()
        {
            byte[] legacy = BJSON.ToBJSON((sbyte)-42);
            legacy[0] = TOKENS.BYTE;

            object? parsed = BJSON.Parse(legacy);

            parsed.Should().BeOfType<byte>();
            parsed.Should().Be((byte)214);
        }

        /// <summary>
        /// A user-defined struct round-trips, which is what made DateTimeOffset's failure specific.
        /// </summary>
        /// <remarks>
        /// Carried over from the DateTimeOffset characterization tests, where it existed to show
        /// that the InvalidProgramException was about that one type and not about structs in general.
        /// It asserted only that writing did not throw; it now asserts the round trip, which is what
        /// it was always standing in for.
        /// </remarks>
        [Test]
        public void UserDefinedStruct_TypedProperty_RoundTrips()
        {
            StructHolder source = new StructHolder { Value = new PlainStruct { Number = 1, Text = "x" } };

            StructHolder restored = BJSON.ToObject<StructHolder>(BJSON.ToBJSON(source))!;

            restored.Value.Number.Should().Be(1);
            restored.Value.Text.Should().Be("x");
        }

        private struct PlainStruct
        {
            public int Number { get; set; }

            public string Text { get; set; }
        }

        private sealed class StructHolder
        {
            public PlainStruct Value { get; set; }
        }

        private sealed class CharHolder
        {
            public char Value { get; set; }
        }

        private sealed class SByteHolder
        {
            public sbyte Value { get; set; }
        }
    }
}
