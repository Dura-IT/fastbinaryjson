using System.Runtime.Serialization;
using AwesomeAssertions;
using DuraIT.FastBinaryJson;
using NUnit.Framework;

namespace FastBinaryJson.UnitTests.RoundTrip
{
    /*
     * The writer encodes each member's key once per encoding and keeps it on the type's cached
     * getters. Every golden case clears the reflection cache first, so none of them writes with a
     * cache the OTHER encoding has already filled. This does: both encodings alternately on one
     * type, each compared with what a fresh cache writes - including a DataMember name and a name
     * long enough for the four-byte length form.
     */
    [TestFixture]
    [TestOf(typeof(BjsonSerializer))]
    public sealed class MemberKeyCacheTests
    {
        [Test]
        public void ToBjson_BothEncodingsAlternately_EachMatchesAFreshCache()
        {
            KeyCacheSubject value = new KeyCacheSubject
            {
                Plain = 1,
                Renamed = 2,
                AMemberNameLongEnoughThatItsUtf16EncodingNeedsTheFourByteLengthFormBecauseItRunsPastTwoHundredAndFiftyFiveBytesOnTheWireXXPadding = 3,
            };
            BjsonParameters utf16 = new BjsonParameters { UseUnicodeStrings = true };
            BjsonParameters utf8 = new BjsonParameters { UseUnicodeStrings = false };

            Bjson.ClearReflectionCache();
            byte[] fresh16 = Bjson.ToBjson(value, utf16);
            Bjson.ClearReflectionCache();
            byte[] fresh8 = Bjson.ToBjson(value, utf8);
            Bjson.ClearReflectionCache();

            foreach (bool unicode in new[] { true, false, true, false })
                Bjson.ToBjson(value, unicode ? utf16 : utf8).Should().Equal(unicode ? fresh16 : fresh8, "unicode = {0}", unicode);

            fresh16.Should().NotEqual(fresh8, "the two encodings must not share one cached key");
            Bjson.ToObject<KeyCacheSubject>(fresh8, utf8).Should().BeEquivalentTo(value);
        }
    }

    public sealed class KeyCacheSubject
    {
        public int Plain { get; set; }

        [DataMember(Name = "renamed_on_the_wire")]
        public int Renamed { get; set; }

        // 129 characters: 258 bytes in UTF-16, past the 255 the one-byte length can carry.
        public int AMemberNameLongEnoughThatItsUtf16EncodingNeedsTheFourByteLengthFormBecauseItRunsPastTwoHundredAndFiftyFiveBytesOnTheWireXXPadding { get; set; }
    }
}
