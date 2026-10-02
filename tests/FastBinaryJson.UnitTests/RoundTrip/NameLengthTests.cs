using System;
using System.Collections.Generic;
using AwesomeAssertions;
using DuraIT.FastBinaryJson;
using NUnit.Framework;

namespace FastBinaryJson.UnitTests.RoundTrip
{
    /*
     * Names of any length, which upstream truncated from 256 encoded bytes onwards.
     *
     * WriteName put the encoded length into a single byte and then wrote `b.Length % 256` bytes, so
     * both wrapped: at the default UseUnicodeStrings = true a 128-character key came back EMPTY, a
     * 200-character one came back as 72 characters, and nothing threw. It serves dictionary keys as
     * well as property names, so it corrupted user data rather than only schema.
     *
     * The fix adds a long form - TOKENS.NAME_LONG and NAME_UNI_LONG, with a four-byte length,
     * mirroring how WriteString has always carried its own length. It is written only from 256
     * encoded bytes onwards, which is what keeps every name that worked before byte-identical; the
     * boundary tests below are what hold that.
     */
    [TestFixture]
    [TestOf(typeof(BJSON))]
    public sealed class NameLengthTests
    {
        /// <summary>
        /// The name of <see cref="LongNameHolder"/>'s only property, 130 characters.
        /// </summary>
        private const string LongPropertyName =
            "ValueWithAPropertyNameLongEnoughToCrossTheTwoHundredAndFiftySixEncodedByteBoundaryWhenItIsWrittenAsUtf16XXXXXXXXXXXXXXXXXXXXXXXXXX";

        [TestCase(1)]
        [TestCase(127)]
        [TestCase(128)]
        [TestCase(200)]
        [TestCase(300)]
        [TestCase(1000)]
        [TestCase(40000)]
        public void DictionaryKey_AtAnyLength_RoundTrips(int keyLength)
        {
            string key = new string('k', keyLength);
            Dictionary<string, string> source = new Dictionary<string, string> { { key, "value" } };

            byte[] bytes = BJSON.ToBJSON(source);
            Dictionary<string, string> restored = BJSON.ToObject<Dictionary<string, string>>(bytes)!;

            restored.Should().ContainKey(key).WhoseValue.Should().Be("value");
        }

        /// <summary>
        /// The same lengths written as UTF-8, where the encoded size is half.
        /// </summary>
        /// <remarks>
        /// Worth running both ways because the threshold is in ENCODED bytes, so the two parameter
        /// sets cross it at different character counts and take the short and long branch for
        /// different inputs. 40000 characters is past what a two-byte length could hold in either
        /// encoding, which is why the long form carries four.
        /// </remarks>
        [TestCase(1)]
        [TestCase(255)]
        [TestCase(256)]
        [TestCase(1000)]
        [TestCase(40000)]
        public void DictionaryKey_AtAnyLength_RoundTripsAsUtf8(int keyLength)
        {
            string key = new string('k', keyLength);
            Dictionary<string, string> source = new Dictionary<string, string> { { key, "value" } };
            BJSONParameters parameters = new BJSONParameters { UseUnicodeStrings = false };

            byte[] bytes = BJSON.ToBJSON(source, parameters);
            Dictionary<string, string> restored = BJSON.ToObject<Dictionary<string, string>>(bytes, parameters)!;

            restored.Should().ContainKey(key).WhoseValue.Should().Be("value");
        }

        /// <summary>
        /// Property names take the same path as dictionary keys, so they were truncated too.
        /// </summary>
        [Test]
        public void PropertyName_LongerThan127Characters_RoundTrips()
        {
            LongNameHolder source = new LongNameHolder();
            source.ValueWithAPropertyNameLongEnoughToCrossTheTwoHundredAndFiftySixEncodedByteBoundaryWhenItIsWrittenAsUtf16XXXXXXXXXXXXXXXXXXXXXXXXXX = 42;

            LongNameHolder restored = BJSON.ToObject<LongNameHolder>(BJSON.ToBJSON(source))!;

            LongPropertyName.Length.Should().Be(130, "the name has to cross the 256 encoded byte threshold as UTF-16");
            restored
                .ValueWithAPropertyNameLongEnoughToCrossTheTwoHundredAndFiftySixEncodedByteBoundaryWhenItIsWrittenAsUtf16XXXXXXXXXXXXXXXXXXXXXXXXXX.Should()
                .Be(42);
        }

        [Test]
        public void Name_Under256EncodedBytes_KeepsTheSingleByteLength()
        {
            byte[] bytes = WriteSingleKey(new string('k', 127), unicode: true);

            bytes[0].Should().Be(TOKENS.DOC_START);
            bytes[1].Should().Be(TOKENS.NAME_UNI, "254 encoded bytes still fits the original form, so these bytes must not move");
            bytes[2].Should().Be(254);
        }

        [Test]
        public void Name_From256EncodedBytes_UsesTheLongFormWithAFourByteLength()
        {
            byte[] bytes = WriteSingleKey(new string('k', 128), unicode: true);

            bytes[0].Should().Be(TOKENS.DOC_START);
            bytes[1].Should().Be(TOKENS.NAME_UNI_LONG);
            BitConverter.ToInt32(bytes, 2).Should().Be(256);
        }

        [Test]
        public void Utf8Name_At255EncodedBytes_KeepsTheSingleByteLength()
        {
            byte[] bytes = WriteSingleKey(new string('k', 255), unicode: false);

            bytes[1].Should().Be(TOKENS.NAME, "255 is the largest length the original single byte can carry");
            bytes[2].Should().Be(255);
        }

        [Test]
        public void Utf8Name_At256EncodedBytes_UsesTheLongForm()
        {
            byte[] bytes = WriteSingleKey(new string('k', 256), unicode: false);

            bytes[1].Should().Be(TOKENS.NAME_LONG);
            BitConverter.ToInt32(bytes, 2).Should().Be(256);
        }

        /// <summary>
        /// Writes a single-pair string dictionary with extensions off, so byte 0 is the document
        /// start and byte 1 is the name token with nothing in between.
        /// </summary>
        private static byte[] WriteSingleKey(string key, bool unicode)
        {
            Dictionary<string, string> source = new Dictionary<string, string> { { key, "v" } };
            BJSONParameters parameters = new BJSONParameters { UseExtensions = false, UseUnicodeStrings = unicode };

            return BJSON.ToBJSON(source, parameters);
        }

        private sealed class LongNameHolder
        {
            public int ValueWithAPropertyNameLongEnoughToCrossTheTwoHundredAndFiftySixEncodedByteBoundaryWhenItIsWrittenAsUtf16XXXXXXXXXXXXXXXXXXXXXXXXXX { get; set; }
        }
    }
}
