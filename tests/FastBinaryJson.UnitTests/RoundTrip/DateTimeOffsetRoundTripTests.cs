using System;

using AwesomeAssertions;

using DuraIT.FastBinaryJson;
using DuraIT.FastBinaryJson.Internal;

using NUnit.Framework;

namespace FastBinaryJson.UnitTests.RoundTrip
{
    /*
     * DateTimeOffset, which upstream could not serialize at all.
     *
     * TOKENS.DATETIMEOFFSET = 27 was declared and never written or read - a dead token. WriteValue
     * had no branch for the type, so it fell through to WriteObject, which emitted a dynamic getter
     * over DateTimeOffset's members and produced invalid IL: InvalidProgramException from generated
     * code, not a clean unsupported-type error.
     *
     * Filling in the dead token is additive. No stream written by any version contains a 27, so
     * nothing that exists today decodes differently.
     *
     * The one real compatibility question is the other direction. Registering a custom type was the
     * ONLY way to store a DateTimeOffset, so anyone who stores one today has a registration and
     * their data is a string. The native branch therefore sits AFTER the custom-type check rather
     * than with the other primitives: a registration still wins, their bytes do not move, and the
     * native form is only reached by callers who never had a working path.
     */
    [TestFixture]
    [TestOf(typeof(BJSON))]
    public sealed class DateTimeOffsetRoundTripTests
    {
        [TearDown]
        public void TearDown()
        {
            // Registration is process-wide, so a test that registers has to undo it or it decides
            // the outcome of every later fixture that touches the same type.
            Reflection.Instance.ClearCustomTypes();
        }

        [TestCase(2)]
        [TestCase(0)]
        [TestCase(-5)]
        [TestCase(14)]
        [TestCase(-12)]
        public void DateTimeOffset_TypedProperty_RoundTrips(int offsetHours)
        {
            DateTimeOffset value = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.FromHours(offsetHours));

            OffsetHolder restored = BJSON.ToObject<OffsetHolder>(BJSON.ToBJSON(new OffsetHolder { Value = value }))!;

            restored.Value.Should().Be(value);
            restored.Value.Offset.Should().Be(value.Offset, "the offset is part of the value, not a rendering of it");
        }

        [Test]
        public void DateTimeOffset_SubSecondTicks_SurviveExactly()
        {
            DateTimeOffset value = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.FromMinutes(330)).AddTicks(1234567);

            OffsetHolder restored = BJSON.ToObject<OffsetHolder>(BJSON.ToBJSON(new OffsetHolder { Value = value }))!;

            restored.Value.Should().Be(value, "the encoding stores raw ticks, and the offset is a whole number of minutes");
        }

        [Test]
        public void DateTimeOffset_Untyped_RoundTripsAsDateTimeOffset()
        {
            DateTimeOffset value = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.FromHours(2));

            object? parsed = BJSON.Parse(BJSON.ToBJSON(value));

            parsed.Should().BeOfType<DateTimeOffset>();
            parsed.Should().Be(value);
        }

        /// <summary>
        /// Pins the encoding: the dead token, the clock ticks, and the offset in whole minutes.
        /// </summary>
        [Test]
        public void DateTimeOffset_Bytes_AreTokenTicksAndOffsetMinutes()
        {
            DateTimeOffset value = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.FromHours(2));

            byte[] bytes = BJSON.ToBJSON(value, new BJSONParameters { UseExtensions = false });

            bytes.Should().HaveCount(11);
            bytes[0].Should().Be(TOKENS.DATETIMEOFFSET, "the token was declared by upstream and left unused");
            BitConverter.ToInt64(bytes, 1).Should().Be(value.Ticks);
            BitConverter.ToInt16(bytes, 9).Should().Be(120, "+02:00 is 120 minutes, written signed so western offsets work");
        }

        /// <summary>
        /// UseUTCDateTime is a DateTime setting and must not touch this type.
        /// </summary>
        /// <remarks>
        /// A DateTime carries no offset, so that parameter exists to decide which clock it means. A
        /// DateTimeOffset already answers the question, and converting it would discard the answer.
        /// </remarks>
        [Test]
        public void DateTimeOffset_UseUTCDateTime_DoesNotShiftTheValue()
        {
            DateTimeOffset value = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.FromHours(2));
            BJSONParameters parameters = new BJSONParameters { UseUTCDateTime = true };

            OffsetHolder restored = BJSON.ToObject<OffsetHolder>(BJSON.ToBJSON(new OffsetHolder { Value = value }, parameters), parameters)!;

            restored.Value.Should().Be(value);
            restored.Value.Offset.Should().Be(TimeSpan.FromHours(2));
        }

        /// <summary>
        /// The compatibility guarantee: an existing registration still decides the bytes.
        /// </summary>
        /// <remarks>
        /// Anyone storing a DateTimeOffset before this fix had to register a custom type, so their
        /// stored data is the string form. If the native branch took precedence their next write
        /// would change format, and worse, their read would break outright - a property whose
        /// declared type is registered is classified Custom, and that path casts the parsed value to
        /// string.
        /// </remarks>
        [Test]
        public void DateTimeOffset_RegisteredCustomType_StillWins()
        {
            DateTimeOffset value = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.FromHours(2));
            BJSON.RegisterCustomType(typeof(DateTimeOffset), x => ((DateTimeOffset)x).ToString("o"), x => DateTimeOffset.Parse(x));

            byte[] bytes = BJSON.ToBJSON(value, new BJSONParameters { UseExtensions = false });

            bytes[0].Should().NotBe(TOKENS.DATETIMEOFFSET, "a registration takes precedence, so the value is written as a string");
            BJSON.ToObject<DateTimeOffset>(bytes).Should().Be(value);
        }

        [Test]
        public void NullableDateTimeOffset_TypedProperty_RoundTrips()
        {
            DateTimeOffset value = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.FromHours(2));

            NullableOffsetHolder restored = BJSON.ToObject<NullableOffsetHolder>(BJSON.ToBJSON(new NullableOffsetHolder { Value = value }))!;

            restored.Value.Should().Be(value);
        }

        [Test]
        public void NullableDateTime_TypedProperty_RoundTrips()
        {
            DateTime value = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Unspecified);

            NullableClockHolder restored = BJSON.ToObject<NullableClockHolder>(BJSON.ToBJSON(new NullableClockHolder { Moment = value }))!;

            restored.Moment.Should().Be(value);
        }

        private sealed class OffsetHolder
        {
            public DateTimeOffset Value { get; set; }
        }

        private sealed class NullableOffsetHolder
        {
            public DateTimeOffset? Value { get; set; }
        }

        private sealed class NullableClockHolder
        {
            public DateTime? Moment { get; set; }
        }
    }
}
