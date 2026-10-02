using System;

using AwesomeAssertions;

using DuraIT.FastBinaryJson;

using NUnit.Framework;

namespace FastBinaryJson.UnitTests.RoundTrip
{
    /*
     * UseUTCDateTime, which upstream applied asymmetrically.
     *
     * The write path sent the value through ToUniversalTime and the read path through ToLocalTime,
     * so a value written as Utc came back as Local, shifted by the READING machine's offset. The
     * bytes were always correct and machine independent; what was broken is that the restored value
     * depended on where it was read. Under TZ=UTC the two cancelled out, which is how it survived.
     *
     * The read now labels the ticks Utc instead of converting them, so the same instant comes back
     * with the same ticks everywhere. No byte moves - the golden fixture utc-datetime is unchanged
     * by this and now asserts its read-back for real instead of pinning bytes only.
     *
     * This is a behaviour change for anyone reading with UseUTCDateTime = true, and the loudest one
     * in this branch: their restored values were being shifted, and now are not.
     */
    [TestFixture]
    [TestOf(typeof(BJSON))]
    public sealed class DateTimeRoundTripTests
    {
        private static readonly DateTime Utc = new DateTime(2026, 1, 2, 3, 4, 5, 678, DateTimeKind.Utc);

        [Test]
        public void UtcDateTime_WrittenAsUtc_ComesBackAsTheSameInstant()
        {
            BJSONParameters parameters = new BJSONParameters { UseUTCDateTime = true };

            ClockHolder restored = BJSON.ToObject<ClockHolder>(BJSON.ToBJSON(new ClockHolder { Moment = Utc }, parameters), parameters)!;

            restored.Moment.Kind.Should().Be(DateTimeKind.Utc, "the ticks written were universal, so that is what they are labelled");
            restored.Moment.Should().Be(Utc);
            restored.Moment.Ticks.Should().Be(Utc.Ticks, "no conversion happens on read, so the reading machine's time zone cannot shift it");
        }

        /// <summary>
        /// A local input is still normalized on write, which is what the parameter is for.
        /// </summary>
        [Test]
        public void UtcDateTime_WrittenAsLocal_ComesBackAsTheSameInstantInUtc()
        {
            DateTime local = new DateTime(2026, 1, 2, 3, 4, 5, 678, DateTimeKind.Local);
            BJSONParameters parameters = new BJSONParameters { UseUTCDateTime = true };

            ClockHolder restored = BJSON.ToObject<ClockHolder>(BJSON.ToBJSON(new ClockHolder { Moment = local }, parameters), parameters)!;

            restored.Moment.Should().Be(local.ToUniversalTime());
            restored.Moment.Kind.Should().Be(DateTimeKind.Utc);
        }

        /// <summary>
        /// The default path is untouched: no conversion either way, ticks stored verbatim.
        /// </summary>
        [Test]
        public void DateTime_WithoutUseUTCDateTime_KeepsItsTicksAndStaysUnspecified()
        {
            DateTime unspecified = new DateTime(2026, 1, 2, 3, 4, 5, 678, DateTimeKind.Unspecified);

            ClockHolder restored = BJSON.ToObject<ClockHolder>(BJSON.ToBJSON(new ClockHolder { Moment = unspecified }))!;

            restored.Moment.Ticks.Should().Be(unspecified.Ticks);
            restored.Moment.Kind.Should().Be(DateTimeKind.Unspecified);
        }

        private sealed class ClockHolder
        {
            public DateTime Moment { get; set; }
        }
    }
}
