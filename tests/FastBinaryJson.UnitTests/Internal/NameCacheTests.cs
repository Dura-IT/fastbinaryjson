using System.Text;
using AwesomeAssertions;
using DuraIT.FastBinaryJson.Internal;
using NUnit.Framework;

namespace FastBinaryJson.UnitTests.Internal
{
    /*
     * net10.0 only: the netstandard2.0 test project removes this file from its compile glob, because
     * the netstandard2.0 build of the library has no NameCache.
     *
     * The cache is process-wide, so these tests use names no other test uses and never assert on
     * how full it is.
     */
    [TestFixture]
    [TestOf(typeof(NameCache))]
    public sealed class NameCacheTests
    {
        [Test]
        public void FromUtf16_SameNameTwice_ReturnsOneInstance()
        {
            byte[] bytes = Encoding.Unicode.GetBytes("xxNameCacheUtf16Probe");

            string first = NameCache.FromUtf16(bytes, 4, bytes.Length - 4);
            string second = NameCache.FromUtf16(bytes, 4, bytes.Length - 4);

            first.Should().Be("NameCacheUtf16Probe");
            second.Should().BeSameAs(first);
        }

        [Test]
        public void FromUtf8_MultiByteName_DecodesAndInterns()
        {
            byte[] bytes = Encoding.UTF8.GetBytes("NameCache€é😀Probe");

            string first = NameCache.FromUtf8(bytes, 0, bytes.Length);

            first.Should().Be("NameCache€é😀Probe");
            NameCache.FromUtf8(bytes, 0, bytes.Length).Should().BeSameAs(first);
        }

        /// <summary>
        /// Invalid UTF-8 must decode exactly as Reflection.UTF8GetString does - same encoder, same
        /// replacement - or a stream would resolve to different member names depending on the path.
        /// </summary>
        [Test]
        public void FromUtf8_InvalidBytes_MatchesTheNonCachedDecoder()
        {
            byte[] bytes = { 0x4E, 0xC3, 0x28, 0xFF, 0x62 };

            NameCache.FromUtf8(bytes, 0, bytes.Length).Should().Be(Reflection.UTF8GetString(bytes, 0, bytes.Length));
        }

        [Test]
        public void FromUtf16_NameLongerThanTheInternLimit_IsReturnedButNotShared()
        {
            string name = new string('q', 200);
            byte[] bytes = Encoding.Unicode.GetBytes(name);

            string first = NameCache.FromUtf16(bytes, 0, bytes.Length);

            first.Should().Be(name);
            NameCache.FromUtf16(bytes, 0, bytes.Length).Should().NotBeSameAs(first);
        }

        [Test]
        public void FromUtf8_NameLongerThanTheInternLimit_IsReturnedButNotShared()
        {
            string name = new string('r', 200);
            byte[] bytes = Encoding.UTF8.GetBytes(name);

            string first = NameCache.FromUtf8(bytes, 0, bytes.Length);

            first.Should().Be(name);
            NameCache.FromUtf8(bytes, 0, bytes.Length).Should().NotBeSameAs(first);
        }
    }
}
