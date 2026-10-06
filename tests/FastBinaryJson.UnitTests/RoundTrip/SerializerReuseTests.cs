// ReSharper disable UnusedAutoPropertyAccessor.Global - reflection-only models: the serializer reads and writes these members, nothing calls them
using System;
using AwesomeAssertions;
using DuraIT.FastBinaryJson;
using NUnit.Framework;

namespace FastBinaryJson.UnitTests.RoundTrip
{
    /*
     * The serializer keeps its $i and $types tables per thread between calls. Whatever one call leaves
     * in them must not reach the next, including when that call threw or called back into the serializer.
     */
    [TestFixture]
    [TestOf(typeof(Bjson))]
    public sealed class SerializerReuseTests
    {
        [Test]
        public void ToBjson_SameGraphTwice_WritesTheSameBytes()
        {
            Parent graph = Build();

            byte[] first = Bjson.ToBjson(graph);
            byte[] second = Bjson.ToBjson(graph);

            second.Should().Equal(first);
        }

        [Test]
        public void ToBjson_AfterADifferentGraph_DoesNotReuseItsIdsOrTypes()
        {
            Parent graph = Build();
            byte[] alone = Bjson.ToBjson(graph);

            Bjson.ToBjson(new Other { Value = 1 });
            byte[] after = Bjson.ToBjson(graph);

            after.Should().Equal(alone);
        }

        [Test]
        public void ToBjson_AfterACallThatThrew_DoesNotReuseItsState()
        {
            Parent graph = Build();
            byte[] alone = Bjson.ToBjson(graph);
            var tooDeep = new Parent();
            Parent current = tooDeep;
            for (int i = 0; i < 40; i++)
            {
                current.Child = new Parent();
                current = current.Child;
            }

            FluentActions.Invoking(() => Bjson.ToBjson(tooDeep)).Should().Throw<BjsonException>();
            byte[] after = Bjson.ToBjson(graph);

            after.Should().Equal(alone);
        }

        [Test]
        public void ToBjson_FromInsideACustomSerializer_KeepsTheOuterCallIntact()
        {
            Bjson.RegisterCustomType(typeof(Reentrant), _ => Convert.ToBase64String(Bjson.ToBjson(new Other { Value = 7 })), _ => new Reentrant());
            var outer = new ReentrantHolder
            {
                First = new Reentrant(),
                Second = new Parent { Name = "x" },
                Third = new Parent { Name = "y" },
            };

            byte[] bytes = Bjson.ToBjson(outer);
            ReentrantHolder? read = Bjson.ToObject<ReentrantHolder>(bytes);

            read!.Second!.Name.Should().Be("x");
            read.Third!.Name.Should().Be("y");
        }

        private static Parent Build()
        {
            var shared = new Other { Value = 3 };
            return new Parent
            {
                Name = "root",
                Child = new Parent { Name = "child", Item = shared },
                Item = shared,
            };
        }

        public sealed class Parent
        {
            public string? Name { get; set; }

            public Parent? Child { get; set; }

            public Other? Item { get; set; }
        }

        public sealed class Other
        {
            public int Value { get; set; }
        }

        public sealed class Reentrant
        {
            public int Tag { get; set; }
        }

        public sealed class ReentrantHolder
        {
            public Reentrant? First { get; set; }

            public Parent? Second { get; set; }

            public Parent? Third { get; set; }
        }
    }
}
