using System.Collections.Generic;
using System.Linq;
using AwesomeAssertions;
using DuraIT.FastBinaryJson;
using NUnit.Framework;

namespace FastBinaryJson.UnitTests.RoundTrip
{
    /*
     * Which objects the writer turns into {"$i": n} back-references.
     *
     * The writer remembers every object it has written and emits a later occurrence as $i. It looked
     * them up with default equality, so a DISTINCT object that compared Equal to an earlier one was
     * written as a reference to it: an entity whose Equals compares only its Id lost every other
     * member on the way through, and equal records came back as one shared instance. $i encodes
     * identity, so only the same instance may become one. Present in upstream 1.6.1 as well.
     *
     * Every case runs on both read paths.
     */
    [TestFixture]
    [TestOf(typeof(BJSONSerializer))]
    public sealed class WriterIdentityTests
    {
        [TestCase(true)]
        [TestCase(false)]
        public void ToBJSON_DistinctEntitiesWithEqualIds_KeepEachOnesMembers(bool oneStep)
        {
            IdHolder value = new IdHolder();
            value.Items.Add(new IdEntity { Id = 1, Name = "original" });
            value.Items.Add(new IdEntity { Id = 1, Name = "edited" });

            IdHolder restored = Read<IdHolder>(BJSON.ToBJSON(value), oneStep);

            restored.Items.Select(i => i.Name).Should().Equal("original", "edited");
            restored.Items[1].Should().NotBeSameAs(restored.Items[0]);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void ToBJSON_DistinctEqualRecords_RestoreAsSeparateInstances(bool oneStep)
        {
            EqRecordsThenShared value = new EqRecordsThenShared
            {
                A = new EqRecord { Name = "same" },
                B = new EqRecord { Name = "same" },
            };

            EqRecordsThenShared restored = Read<EqRecordsThenShared>(BJSON.ToBJSON(value), oneStep);

            restored.A!.Name.Should().Be("same");
            restored.B!.Name.Should().Be("same");
            restored.B.Should().NotBeSameAs(restored.A);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void ToBJSON_SameInstanceTwice_RestoresOneInstance(bool oneStep)
        {
            IdEntity shared = new IdEntity { Id = 7, Name = "shared" };
            IdHolder value = new IdHolder();
            value.Items.Add(shared);
            value.Items.Add(shared);

            IdHolder restored = Read<IdHolder>(BJSON.ToBJSON(value), oneStep);

            restored.Items[0].Name.Should().Be("shared");
            restored.Items[1].Should().BeSameAs(restored.Items[0]);
        }

        private static T Read<T>(byte[] bytes, bool oneStep)
        {
            return new Deserializer(new BJSONParameters()) { OneStep = oneStep }.ToObject<T>(bytes)!;
        }
    }

    /// <summary>
    /// Identity by Id, as entity classes commonly implement it.
    /// </summary>
    public sealed class IdEntity
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public override bool Equals(object? obj) => obj is IdEntity other && other.Id == Id;

        public override int GetHashCode() => Id;
    }

    public sealed class IdHolder
    {
        public List<IdEntity> Items { get; set; } = new List<IdEntity>();
    }
}
