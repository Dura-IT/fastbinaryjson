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
        #region name truncation

        /*
         * DEFECT: names of 256 encoded bytes or more are silently truncated.
         *
         * BJsonSerializer.WriteName writes a SINGLE byte length prefix and then writes
         * `b.Length % 256` bytes. Both wrap. With the default UseUnicodeStrings = true a name is
         * UTF-16, so the limit is 128 CHARACTERS, not 256; with UseUnicodeStrings = false it is 256
         * ASCII characters.
         *
         * WriteName serves both property names and dictionary keys, so this corrupts user data,
         * not just schema. Nothing throws - the key comes back short, or empty, and the value is
         * still attached to it.
         *
         * Observed with the default parameters:
         *   127 chars -> 254 bytes -> intact
         *   128 chars -> 256 bytes -> key restored EMPTY
         *   200 chars -> 400 bytes -> key restored as 72 chars
         */
        [TestCase(120, 120)]
        [TestCase(127, 127)]
        [TestCase(128, 0)]
        [TestCase(200, 72)]
        [TestCase(300, 44)]
        public void DictionaryKey_LongerThan127Characters_IsSilentlyTruncated(int keyLength, int restoredLength)
        {
            string key = new string('k', keyLength);
            Dictionary<string, string> source = new Dictionary<string, string> { { key, "value" } };

            byte[] bytes = BJSON.ToBJSON(source);
            Dictionary<string, string> restored = BJSON.ToObject<Dictionary<string, string>>(bytes)!;

            restored.Should().ContainSingle("the pair survives; only the key is damaged");
            foreach (KeyValuePair<string, string> pair in restored)
            {
                pair.Key.Length.Should().Be(restoredLength);
                pair.Value.Should().Be("value", "only the name is truncated, never the value");
            }
        }

        [Test]
        public void DictionaryKey_LongerThan127Characters_SurvivesWhenWrittenAsUtf8()
        {
            // Same key, same defect, different threshold: UTF-8 halves the encoded size for ASCII,
            // so 200 characters now fits under 256 bytes. This is a workaround, not a fix - a
            // 256-character ASCII key corrupts under these parameters too.
            string key = new string('k', 200);
            Dictionary<string, string> source = new Dictionary<string, string> { { key, "value" } };
            BJSONParameters parameters = new BJSONParameters { UseUnicodeStrings = false };

            byte[] bytes = BJSON.ToBJSON(source, parameters);
            Dictionary<string, string> restored = BJSON.ToObject<Dictionary<string, string>>(bytes, parameters)!;

            restored.Should().ContainKey(key);
        }

        #endregion

        #region DateTimeOffset

        /*
         * DEFECT: DateTimeOffset cannot be serialized at all.
         *
         * TOKENS.DATETIMEOFFSET = 27 is declared in BJSON.cs and is never written or read anywhere
         * in the library - it is a dead token. BJsonSerializer.WriteValue has no DateTimeOffset
         * branch, so the value falls through to WriteObject, which emits a dynamic getter over
         * DateTimeOffset's members and produces invalid IL.
         *
         * Effect: InvalidProgramException from generated code, not a clean "unsupported type"
         * error. Registering a custom type is the only way to store a DateTimeOffset today, which
         * is what the ported `datetimeoff` test does - and why that test passes while this fails.
         *
         * A user-defined struct serializes fine, so this is specific to DateTimeOffset rather than
         * a general problem with structs.
         */
        [Test]
        public void DateTimeOffset_Property_ThrowsInvalidProgramException()
        {
            OffsetHolder value = new OffsetHolder { Value = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.FromHours(2)) };

            Action write = () => BJSON.ToBJSON(value);

            write.Should().Throw<InvalidProgramException>("WriteValue has no DateTimeOffset branch and the emitted getter is invalid");
        }

        [Test]
        public void PlainStruct_Property_SerializesFine()
        {
            StructHolder value = new StructHolder { Value = new PlainStruct { Number = 1, Text = "x" } };

            Action write = () => BJSON.ToBJSON(value);

            write.Should().NotThrow("the DateTimeOffset failure is specific to that type, not to structs");
        }

        #endregion

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

        private sealed class OffsetHolder
        {
            public DateTimeOffset Value { get; set; }
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
