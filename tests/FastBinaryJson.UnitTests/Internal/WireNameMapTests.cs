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
        private static readonly PropertyMetadata Name = new PropertyMetadata();

        private static readonly PropertyMetadata Age = new PropertyMetadata();

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

        [Test]
        public void Remember_Keys_AreKeptInFirstReadOrder()
        {
            WireNameMap map = CreateMap();

            map.Remember(new byte[] { 1 }, Name, false);
            map.Remember(new byte[] { 2 }, Age, false);
            map.Remember(new byte[] { 3 }, null, true);

            map.Keys.Select(k => k.Raw[0]).Should().Equal(1, 2, 3);
            map.Keys[1].Member.Should().BeSameAs(Age);
            map.Keys[2].Special.Should().BeTrue();
        }

        [Test]
        public void Remember_PastTheCap_IsIgnored()
        {
            WireNameMap map = CreateMap();

            for (int i = 0; i < 500; i++)
                map.Remember(new[] { (byte)i }, null, false);

            map.Keys.Length.Should().Be(32, "two members give the minimum cap of 32");
        }

        [Test]
        public void Keys_SnapshotTakenBeforeRemember_IsUnchanged()
        {
            WireNameMap map = CreateMap();
            map.Remember(new byte[] { 1 }, Name, false);
            WireKey[] snapshot = map.Keys;

            map.Remember(new byte[] { 2 }, Age, false);

            snapshot.Should().HaveCount(1);
            map.Keys.Should().HaveCount(2);
        }

        private static WireNameMap CreateMap()
        {
            return new WireNameMap(new Dictionary<string, PropertyMetadata> { ["name"] = Name, ["age"] = Age });
        }
    }
}
