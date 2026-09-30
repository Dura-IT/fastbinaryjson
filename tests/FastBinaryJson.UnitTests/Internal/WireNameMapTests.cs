using System.Collections.Generic;
using System.Linq;

using AwesomeAssertions;

using DuraIT.FastBinaryJson.Internal;

using NUnit.Framework;

namespace FastBinaryJson.UnitTests.Internal
{
    [TestFixture]
    [TestOf(typeof(WireNameMap))]
    public sealed class WireNameMapTests
    {
        private static readonly myPropInfo Name = new myPropInfo();

        private static readonly myPropInfo Age = new myPropInfo();

        [TestCase("name")]
        [TestCase("Name")]
        [TestCase("NAME")]
        public void Find_AnySpellingOfAMember_ResolvesLikeTheLowercasedLookup(string wireName)
        {
            WireNameMap map = CreateMap();

            map.Find(wireName).Should().BeSameAs(Name);
            map.Find(wireName).Should().BeSameAs(Name, "the second lookup is served from the cache");
        }

        [Test]
        public void Find_UnknownName_ReturnsNull()
        {
            WireNameMap map = CreateMap();

            map.Find("missing").Should().BeNull();
            map.Find("missing").Should().BeNull("a cached miss stays a miss");
        }

        /// <summary>
        /// Keys are chosen by whoever wrote the stream. Past the cap they stop being cached, but every
        /// lookup must still give the right answer.
        /// </summary>
        [Test]
        public void Find_PastTheCap_StillResolvesEveryName()
        {
            WireNameMap map = CreateMap();

            foreach (string junk in Enumerable.Range(0, 500).Select(i => "junk" + i))
                map.Find(junk).Should().BeNull();

            map.Find("AGE").Should().BeSameAs(Age);
            map.Find("Name").Should().BeSameAs(Name);
        }

        private static WireNameMap CreateMap()
        {
            return new WireNameMap(new Dictionary<string, myPropInfo> { ["name"] = Name, ["age"] = Age });
        }
    }
}
