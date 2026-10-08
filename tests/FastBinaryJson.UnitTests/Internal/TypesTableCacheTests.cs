using System.Collections.Generic;
using AwesomeAssertions;
using DuraIT.FastBinaryJson;
using DuraIT.FastBinaryJson.Internal;
using FastBinaryJson.Benchmarks.Corpus;
using NUnit.Framework;

namespace FastBinaryJson.UnitTests.Internal
{
    [TestFixture]
    [TestOf(typeof(TypesTableCache))]
    public sealed class TypesTableCacheTests
    {
        private static Dictionary<string, object> Table(string name)
        {
            return new Dictionary<string, object> { [name] = "1" };
        }

        private static byte[] Payload(byte tableByte, int tableLength)
        {
            // Four bytes of head, then the table, which runs to the end like the writer's does.
            byte[] json = new byte[4 + tableLength];
            for (int i = 4; i < json.Length; i++)
                json[i] = tableByte;
            return json;
        }

        [Test]
        public void TryGet_SameBytesAfterRemember_ReturnsTheMaster()
        {
            byte[] json = Payload(7, 20);
            Dictionary<string, object> master = Table("a");
            TypesTableCache.Remember(json, 4, master);

            bool hit = TypesTableCache.TryGet(Payload(7, 20), 4, out Dictionary<string, object>? types);

            hit.Should().BeTrue();
            types.Should().BeSameAs(master);
        }

        [Test]
        public void TryGet_SameLengthDifferentBytes_Misses()
        {
            TypesTableCache.Remember(Payload(7, 20), 4, Table("a"));

            TypesTableCache.TryGet(Payload(8, 20), 4, out _).Should().BeFalse();
        }

        [Test]
        public void TryGet_DifferentLength_Misses()
        {
            TypesTableCache.Remember(Payload(7, 20), 4, Table("a"));

            TypesTableCache.TryGet(Payload(7, 21), 4, out _).Should().BeFalse();
        }

        [TestCase(-1)]
        [TestCase(0)]
        [TestCase(24)]
        [TestCase(int.MaxValue)]
        public void TryGet_PointerOutOfRange_MissesWithoutThrowing(int pointer)
        {
            TypesTableCache.Remember(Payload(7, 20), 4, Table("a"));

            TypesTableCache.TryGet(Payload(7, 20), pointer, out _).Should().BeFalse();
        }

        [Test]
        public void Remember_TableLargerThanTheLimit_IsNotKept()
        {
            byte[] big = Payload(7, 8192);
            TypesTableCache.Remember(Payload(1, 20), 4, Table("small"));
            TypesTableCache.Remember(big, 4, Table("big"));

            // The earlier entry is untouched and the oversized one never entered.
            TypesTableCache.TryGet(big, 4, out _).Should().BeFalse();
            TypesTableCache.TryGet(Payload(1, 20), 4, out _).Should().BeTrue();
        }

        [Test]
        public void ToObject_TypeGraphsInterleaved_EachReadsBackItsOwn()
        {
            BjsonParameters parameters = new BjsonParameters { UseUnicodeStrings = true };
            FlatPrimitives flat = PayloadFactory.CreateFlatPrimitives();
            Order order = PayloadFactory.CreateNestedOrder();
            ShapeCatalogue shapes = PayloadFactory.CreateShapeCatalogue();
            byte[] flatBytes = Bjson.ToBjson(flat, parameters);
            byte[] orderBytes = Bjson.ToBjson(order, parameters);
            byte[] shapeBytes = Bjson.ToBjson(shapes, parameters);

            // Each round changes the table in the cache, so a stale hit would hand one graph another's types.
            for (int round = 0; round < 3; round++)
            {
                Bjson.ToObject<FlatPrimitives>(flatBytes, parameters).Should().BeEquivalentTo(flat);
                Bjson.ToObject<FlatPrimitives>(flatBytes, parameters).Should().BeEquivalentTo(flat);
                Bjson.ToObject<Order>(orderBytes, parameters).Should().BeEquivalentTo(order);
                Bjson.ToObject<ShapeCatalogue>(shapeBytes, parameters).Should().BeEquivalentTo(shapes);
                Bjson.ToObject<ShapeCatalogue>(shapeBytes, parameters).Should().BeEquivalentTo(shapes);
            }
        }

        [Test]
        public void ToObject_RepeatedRead_ReturnsIndependentObjects()
        {
            BjsonParameters parameters = new BjsonParameters { UseUnicodeStrings = true };
            byte[] bytes = Bjson.ToBjson(PayloadFactory.CreateFlatPrimitives(), parameters);

            FlatPrimitives first = Bjson.ToObject<FlatPrimitives>(bytes, parameters)!;
            FlatPrimitives second = Bjson.ToObject<FlatPrimitives>(bytes, parameters)!;

            second.Should().NotBeSameAs(first);
            second.Should().BeEquivalentTo(first);
        }
    }
}
