using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using AwesomeAssertions;
using DuraIT.FastBinaryJson;
using NUnit.Framework;

namespace FastBinaryJson.UnitTests.RoundTrip
{
    /*
     * String writes on net10.0 count the UTF-8 bytes, write the length header, then encode straight
     * into the pooled output buffer; UTF-16 is written straight from the string's memory. The
     * golden files only hold short strings, so they never make the output grow in the middle of a
     * string. These cases do, and pin the output to bytes built independently of the serializer.
     *
     * The same source compiles into the netstandard2.0 test project, where it pins upstream's
     * byte[] path to the same expectations - so the two targets are held to one answer.
     */
    [TestFixture]
    [TestOf(typeof(Bjson))]
    public sealed class StringEncodingTests
    {
        private static readonly BjsonParameters Utf8 = new BjsonParameters { UseUnicodeStrings = false };

        private static readonly BjsonParameters Utf16 = new BjsonParameters { UseUnicodeStrings = true };

        // One, two, three and four UTF-8 bytes per code point, so byte count and char count diverge.
        private static readonly string[] Pieces = { "a", "é", "€", "😀" };

        [Test]
        public void ToBjson_LongUtf8String_WritesExactUtf8Bytes()
        {
            string value = MixedText(5000);

            byte[] bytes = Bjson.ToBjson(value, Utf8);

            bytes.Should().Equal(Expected(Tokens.Utf8String, new UTF8Encoding().GetBytes(value)));
        }

        [Test]
        public void ToBjson_Utf16String_WritesExactUtf16Bytes()
        {
            string value = MixedText(5000);

            byte[] bytes = Bjson.ToBjson(value, Utf16);

            bytes.Should().Equal(Expected(Tokens.Utf16String, Encoding.Unicode.GetBytes(value)));
        }

        /// <summary>
        /// Upstream encodes with a plain UTF8Encoding, whose fallback replaces a lone surrogate with
        /// U+FFFD. The span path must make the same substitution, not throw and not drop it.
        /// </summary>
        [Test]
        public void ToBjson_Utf8LoneSurrogate_ReplacesLikeUpstream()
        {
            string value = "a\uD800b";

            byte[] bytes = Bjson.ToBjson(value, Utf8);

            bytes.Should().Equal(Expected(Tokens.Utf8String, new byte[] { 0x61, 0xEF, 0xBF, 0xBD, 0x62 }));
        }

        [TestCaseSource(nameof(BothEncodings))]
        public void ToBjson_StringsOfMixedLengthInOneGraph_RoundTripEachExactly(BjsonParameters parameters)
        {
            List<string> values = new List<string> { MixedText(3000), "short", MixedText(700), string.Empty, "é", MixedText(4000) };

            object? restored = Bjson.Parse(Bjson.ToBjson(values, parameters));

            restored.Should().BeAssignableTo<List<object>>().Which.Should().Equal(values);
        }

        /// <summary>
        /// Names are encoded the same way as values, and long ones take the four-byte length form.
        /// Interleaving short and long keys with long values grows the output across both paths.
        /// </summary>
        [TestCaseSource(nameof(BothEncodings))]
        public void ToBjson_LongAndShortKeysInterleaved_RoundTripEachExactly(BjsonParameters parameters)
        {
            Dictionary<string, string> values = new Dictionary<string, string>
            {
                [MixedText(400)] = "a",
                ["b"] = MixedText(2000),
                [MixedText(90)] = MixedText(10),
                ["c"] = string.Empty,
            };

            object? restored = Bjson.Parse(Bjson.ToBjson(values, parameters));

            restored.Should().BeAssignableTo<Dictionary<string, object>>().Which.Should().Equal(values.ToDictionary(x => x.Key, x => (object)x.Value));
        }

        private static IEnumerable<TestCaseData> BothEncodings()
        {
            yield return new TestCaseData(Utf8).SetArgDisplayNames("utf8");
            yield return new TestCaseData(Utf16).SetArgDisplayNames("utf16");
        }

        private static string MixedText(int length)
        {
            StringBuilder builder = new StringBuilder(length + 1);
            int piece = 0;
            while (builder.Length < length)
                builder.Append(Pieces[piece++ % Pieces.Length]);

            return builder.ToString();
        }

        // Token, native-order Int32 length, then the payload - the layout WriteString produces.
        private static byte[] Expected(byte token, byte[] payload)
        {
            return new[] { token }.Concat(BitConverter.GetBytes(payload.Length)).Concat(payload).ToArray();
        }
    }
}
