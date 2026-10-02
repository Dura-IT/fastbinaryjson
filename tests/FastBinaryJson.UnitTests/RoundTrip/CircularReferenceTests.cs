using AwesomeAssertions;

using DuraIT.FastBinaryJson;

using NUnit.Framework;

namespace FastBinaryJson.UnitTests.RoundTrip
{
    /*
     * $i numbering on read against the writer's.
     *
     * The writer gives every object it writes a number, in preorder, and writes a later occurrence of
     * one as {"$i": n}. The reader has to number the objects it creates in the same order, or every
     * $i after the first divergence resolves to the wrong object.
     *
     * Every case runs on both read paths: they share the numbering.
     */
    [TestFixture]
    [TestOf(typeof(Deserializer))]
    public sealed class CircularReferenceTests
    {
        /// <summary>
        /// Two struct members, then a shared reference. Each struct's box starts out all zeros, so the
        /// second one Equals the first when it is created.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        public void ToObject_TwoStructsThenSharedReference_KeepsTheReference(bool oneStep)
        {
            EqNode shared = new EqNode { Name = "shared" };
            EqStructsThenShared value = new EqStructsThenShared { A = new EqPoint { X = 1, Y = 2 }, B = new EqPoint { X = 3, Y = 4 }, First = shared, Second = shared };

            EqStructsThenShared restored = Read<EqStructsThenShared>(BJSON.ToBJSON(value), oneStep);

            restored.A.Should().Be(value.A);
            restored.B.Should().Be(value.B);
            restored.First!.Name.Should().Be("shared");
            restored.Second.Should().BeSameAs(restored.First);
        }

        /// <summary>
        /// Two equal structs. The writer used to write the second as $i to the first and writes both in
        /// full now; GoldenFileTests.LegacyEqualStructs_CommittedBytes_RestoreBothValues keeps the old
        /// bytes covered.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        public void ToObject_EqualStructs_BothKeepTheirValue(bool oneStep)
        {
            EqStructsThenShared value = new EqStructsThenShared { A = new EqPoint { X = 5, Y = 6 }, B = new EqPoint { X = 5, Y = 6 } };

            EqStructsThenShared restored = Read<EqStructsThenShared>(BJSON.ToBJSON(value), oneStep);

            restored.A.Should().Be(value.A);
            restored.B.Should().Be(value.B);
        }

        /// <summary>
        /// A record with value equality: an empty one, then a filled one. The filled one Equals the
        /// empty one at the moment it is created, before its members are read.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        public void ToObject_EmptyRecordThenFilledRecordThenSharedReference_KeepsTheReference(bool oneStep)
        {
            EqNode shared = new EqNode { Name = "shared" };
            EqRecordsThenShared value = new EqRecordsThenShared { A = new EqRecord(), B = new EqRecord { Name = "b" }, First = shared, Second = shared };

            EqRecordsThenShared restored = Read<EqRecordsThenShared>(BJSON.ToBJSON(value), oneStep);

            restored.B!.Name.Should().Be("b");
            restored.First!.Name.Should().Be("shared");
            restored.Second.Should().BeSameAs(restored.First);
        }

        private static T Read<T>(byte[] bytes, bool oneStep)
        {
            return (T)new Deserializer(new BJSONParameters()) { OneStep = oneStep }.ToObject(bytes, typeof(T))!;
        }
    }

    public sealed class EqStructsThenShared
    {
        public EqPoint A { get; set; }

        public EqPoint B { get; set; }

        public EqNode? First { get; set; }

        public EqNode? Second { get; set; }
    }

    public sealed record EqRecord
    {
        public string? Name { get; set; }
    }

    public sealed class EqRecordsThenShared
    {
        public EqRecord? A { get; set; }

        public EqRecord? B { get; set; }

        public EqNode? First { get; set; }

        public EqNode? Second { get; set; }
    }
}
