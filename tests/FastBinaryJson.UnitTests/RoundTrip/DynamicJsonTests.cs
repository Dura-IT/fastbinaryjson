using System.Collections.Generic;
using AwesomeAssertions;
using DuraIT.FastBinaryJson;
using NUnit.Framework;

namespace FastBinaryJson.UnitTests.RoundTrip
{
    /*
     * A dynamic member is looked up as written first, then ignoring case. Upstream had the same fallback
     * but returned whether the EXACT name existed, so a key found by the fallback was still reported
     * missing and the binder threw: the fallback never worked.
     */
    [TestFixture]
    [TestOf(typeof(BJSON))]
    public sealed class DynamicJsonTests
    {
        [Test]
        public void ToDynamic_MemberWrittenInOtherCase_IsFound()
        {
            byte[] bytes = BJSON.ToBJSON(new Dictionary<string, object> { ["Name"] = "value" });

            dynamic parsed = BJSON.ToDynamic(bytes);

            ((string)parsed.Name).Should().Be("value");
            ((string)parsed.name).Should().Be("value");
            ((string)parsed.NAME).Should().Be("value");
        }
    }
}
