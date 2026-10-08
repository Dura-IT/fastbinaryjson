using System.Collections.Specialized;
using AwesomeAssertions;
using DuraIT.FastBinaryJson;
using NUnit.Framework;

namespace FastBinaryJson.UnitTests.RoundTrip
{
    /*
     * A NameValueCollection may hold a null key. The netstandard2.0 build wrote it as an empty name
     * before string encoding went through the shared path, so serializing must still not throw.
     */
    [TestFixture]
    [TestOf(typeof(Bjson))]
    public sealed class NullKeyTests
    {
        [TestCase(true)]
        [TestCase(false)]
        public void ToBjson_NameValueCollectionWithNullKey_DoesNotThrow(bool unicode)
        {
            NameValueCollection collection = new NameValueCollection { { null, "value" } };

            Bjson.ToBjson(collection, new BjsonParameters { UseUnicodeStrings = unicode }).Should().NotBeEmpty();
        }
    }
}
