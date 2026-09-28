using System;
using System.Collections.Generic;
using System.Net;

using AwesomeAssertions;

using DuraIT.FastBinaryJson;

using NUnit.Framework;

namespace FastBinaryJson.UnitTests.Defects
{
    /*
     * Characterization tests for defects inherited from upstream.
     *
     * READ THIS BEFORE "FIXING" A FAILURE HERE.
     *
     * Every test in this file asserts behaviour that is WRONG. They exist so the defects are
     * recorded in executable form rather than in prose, and so that the moment any of them is
     * actually fixed, the corresponding test fails loudly and forces the author to replace it with
     * an assertion of the correct behaviour. A red test in this file is therefore good news: it
     * means a defect was fixed. Do not "repair" it by adjusting the expectation to match the new
     * output; delete it and write the real test instead.
     *
     * Each defect carries the observed threshold or exception type rather than a summary, because
     * the specifics are what a fix has to change and what a consumer has to work around.
     */
    [TestFixture]
    [TestOf(typeof(BJSON))]
    public sealed class KnownDefectTests
    {
        #region UTC date-time round trip

        /*
         * DEFECT: UseUTCDateTime does not round-trip a DateTime.
         *
         * The write path sends the value through ToUniversalTime and the read path through
         * ToLocalTime, so a value written as Utc comes back as Local, shifted by the reading
         * machine's offset. The BYTES are correct and machine independent - what is broken is the
         * asymmetry, which makes the restored value depend on where it is read.
         *
         * Under TZ=UTC the two cancel out and nothing looks wrong, which is why this survives.
         * The golden fixture utc-datetime therefore pins the bytes only, and defers to this test
         * for the read-back.
         *
         * Expressed against TimeZoneInfo.Local rather than a hardcoded offset, so it characterizes
         * the same defect in every time zone instead of passing only in one.
         */
        [Test]
        public void UtcDateTime_RoundTrip_ReturnsLocalTime()
        {
            DateTime written = new DateTime(2026, 1, 2, 3, 4, 5, 678, DateTimeKind.Utc);
            BJSONParameters parameters = new BJSONParameters { UseUTCDateTime = true };

            byte[] bytes = BJSON.ToBJSON(new ClockHolder { Moment = written }, parameters);
            ClockHolder restored = BJSON.ToObject<ClockHolder>(bytes, parameters)!;

            restored.Moment.Kind.Should().Be(DateTimeKind.Local, "the read path calls ToLocalTime regardless of how the value was written");
            restored.Moment.Should().Be(written.ToLocalTime(), "the value is shifted by the reading machine's UTC offset");

            // Stated separately so the consequence is not lost in a green run on a UTC machine.
            if (TimeZoneInfo.Local.GetUtcOffset(written) != TimeSpan.Zero)
            {
                restored.Moment.Should().NotBe(written, "a value written as Utc does not come back as the same instant's Utc representation");
            }
        }

        #endregion

        #region custom type registration

        /*
         * DEFECT: a registered custom type does not apply to subclasses.
         *
         * Reflection.IsTypeRegistered does an exact-type dictionary lookup on obj.GetType(), so a
         * registration for a base type is skipped for any derived instance and serialization falls
         * through to reflection.
         *
         * This is not academic. IPAddress.Loopback on .NET returns the private subclass
         * System.Net.IPAddress+ReadOnlyIPAddress, so the single most obvious way to obtain an
         * IPAddress misses a registration for typeof(IPAddress). Reflection then reaches
         * IPAddress.ScopeId, which throws SocketException for any IPv4 address.
         *
         * On .NET Framework 4.0, where upstream was written, Loopback was a plain IPAddress and
         * the exact match held - which is why the upstream test never caught this.
         */
        [Test]
        public void CustomType_AppliesToExactTypeOnly()
        {
            BJSON.RegisterCustomType(typeof(IPAddress), x => x.ToString()!, x => IPAddress.Parse(x));

            Action exact = () => BJSON.ToBJSON(new AddressHolder { Value = new IPAddress(new byte[] { 127, 0, 0, 1 }) });
            Action derived = () => BJSON.ToBJSON(new AddressHolder { Value = IPAddress.Loopback });

            IPAddress.Loopback.GetType().Should().NotBe(typeof(IPAddress), "Loopback is a private ReadOnlyIPAddress subclass on .NET");
            exact.Should().NotThrow();
            derived.Should().Throw<System.Net.Sockets.SocketException>("the registration is skipped and reflection reaches ScopeId");
        }

        #endregion

        private sealed class AddressHolder
        {
            public IPAddress Value { get; set; } = null!;
        }

        private sealed class ClockHolder
        {
            public DateTime Moment { get; set; }
        }
    }
}
