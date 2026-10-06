using System.Collections.Generic;
using AwesomeAssertions;
using DuraIT.FastBinaryJson;
using NUnit.Framework;

namespace FastBinaryJson.UnitTests.RoundTrip
{
    /*
     * The one-step reader recognises a member key by comparing its bytes with keys its type has seen
     * before, and decodes the name only for a key it has not seen. These pin the cases where a
     * remembered key is NOT the one at the expected position: another encoding, members skipped for
     * being null, another member order, and a $ key that must keep sending the object to the two-step
     * path.
     *
     * Each test uses its own types, because the remembered keys are per type and process-wide.
     */
    [TestFixture]
    [TestOf(typeof(TypedReader))]
    public sealed class MemberKeyTests
    {
        [Test]
        public void ToObject_SameTypeInBothEncodings_ReadsEachCorrectly()
        {
            List<KeyEncodingNode> value = new List<KeyEncodingNode>
            {
                new KeyEncodingNode
                {
                    Name = "a",
                    NameAlt = "x",
                    Value = 1,
                },
                new KeyEncodingNode
                {
                    Name = "b",
                    NameAlt = "y",
                    Value = 2,
                },
            };

            foreach (bool unicode in new[] { true, false, true })
            {
                BJSONParameters parameters = new BJSONParameters { UseUnicodeStrings = unicode };

                List<KeyEncodingNode> restored = BJSON.ToObject<List<KeyEncodingNode>>(BJSON.ToBJSON(value, parameters), parameters)!;

                restored.Should().BeEquivalentTo(value, o => o.WithStrictOrdering(), "unicode = {0}", unicode);
            }
        }

        /// <summary>
        /// Null members are not written, so a key arrives where the type's previous object had another
        /// one - here also a name that the remembered shorter name is a prefix of.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        public void ToObject_MembersSkippedForNull_ReadsEveryObject(bool unicode)
        {
            BJSONParameters parameters = new BJSONParameters { UseUnicodeStrings = unicode };
            List<KeySkipNode> value = new List<KeySkipNode>
            {
                new KeySkipNode
                {
                    Name = "a",
                    NameAlt = "x",
                    Value = 1,
                },
                new KeySkipNode
                {
                    Name = null,
                    NameAlt = "y",
                    Value = 2,
                },
                new KeySkipNode
                {
                    Name = "c",
                    NameAlt = null,
                    Value = 3,
                },
                new KeySkipNode
                {
                    Name = null,
                    NameAlt = null,
                    Value = 4,
                },
                new KeySkipNode
                {
                    Name = "e",
                    NameAlt = "z",
                    Value = 5,
                },
            };

            List<KeySkipNode> restored = BJSON.ToObject<List<KeySkipNode>>(BJSON.ToBJSON(value, parameters), parameters)!;

            restored.Should().BeEquivalentTo(value, o => o.WithStrictOrdering());
        }

        /// <summary>
        /// The writer always puts a type's members in one order, but stored input need not come from
        /// it. Written as a type that declares the members the other way round, read after the
        /// normal order has been remembered.
        /// </summary>
        [Test]
        public void ToObject_MembersInAnotherOrder_ReadsEachByName()
        {
            BJSONParameters parameters = new BJSONParameters { UseExtensions = false, UsingGlobalTypes = false };
            byte[] inOrder = BJSON.ToBJSON(new KeyOrderAB { First = 1, Second = "one" }, parameters);
            byte[] reversed = BJSON.ToBJSON(new KeyOrderBA { Second = "two", First = 2 }, parameters);

            KeyOrderAB first = BJSON.ToObject<KeyOrderAB>(inOrder, parameters)!;
            KeyOrderAB second = BJSON.ToObject<KeyOrderAB>(reversed, parameters)!;
            KeyOrderAB third = BJSON.ToObject<KeyOrderAB>(inOrder, parameters)!;

            first.Should().BeEquivalentTo(new KeyOrderAB { First = 1, Second = "one" });
            second.Should().BeEquivalentTo(new KeyOrderAB { First = 2, Second = "two" });
            third.Should().BeEquivalentTo(new KeyOrderAB { First = 1, Second = "one" });
        }

        /// <summary>
        /// A $ key after the head sends the object to the two-step path. The second read finds the key
        /// remembered, and must still do so.
        /// </summary>
        [Test]
        public void ToObject_SpecialKeyAfterTheHead_FallsBackEveryTime()
        {
            BJSONParameters parameters = new BJSONParameters { UseExtensions = false, UsingGlobalTypes = false };
            byte[] bytes = BJSON.ToBJSON(new Dictionary<string, object> { ["First"] = 7, ["$x"] = 8 }, parameters);

            for (int read = 0; read < 2; read++)
            {
                Deserializer deserializer = new Deserializer(parameters);

                KeySpecial restored = deserializer.ToObject<KeySpecial>(bytes)!;

                restored.First.Should().Be(7);
                deserializer.OneStepFallbacks.Should().Be(1, "read {0} has to leave the object to the two-step path", read);
            }
        }
    }

    public sealed class KeyEncodingNode
    {
        public string? Name { get; set; }

        public string? NameAlt { get; set; }

        public int Value { get; set; }
    }

    public sealed class KeySkipNode
    {
        public string? Name { get; set; }

        public string? NameAlt { get; set; }

        public int Value { get; set; }
    }

    public sealed class KeyOrderAB
    {
        public int First { get; set; }

        public string? Second { get; set; }
    }

    public sealed class KeyOrderBA
    {
        public string? Second { get; set; }

        public int First { get; set; }
    }

    public sealed class KeySpecial
    {
        public int First { get; set; }

        public int Second { get; set; }
    }
}
