using AwesomeAssertions;
using DuraIT.FastBinaryJson;
using NUnit.Framework;

namespace FastBinaryJson.UnitTests.RoundTrip
{
    /*
     * FillObject with a class-typed nested member. ParseDictionary handed its own `input` - the root
     * instance being filled - down to the nested call, and a non-null input was used instead of
     * creating a new instance, so the nested member's setters ran against the root and threw
     * InvalidCastException. Upstream's two FillObject tests assert nothing and never populate a
     * class-typed member, so that path had no coverage at all.
     *
     * Nested members now get a new instance, the same as ToObject gives them.
     */
    [TestFixture]
    [TestOf(typeof(BJSON))]
    public sealed class FillObjectTests
    {
        [TestCase(true)]
        [TestCase(false)]
        public void FillObject_NestedClassMember_IsFilledIntoItsOwnInstance(bool useExtensions)
        {
            BJSONParameters parameters = new BJSONParameters { UseExtensions = useExtensions };
            byte[] bytes = BJSON.ToBJSON(
                new Outer
                {
                    Name = "outer",
                    Inner = new Inner { Value = 5 },
                },
                parameters
            );

            Outer target = new Outer();
            BJSON.FillObject(target, bytes);

            target.Name.Should().Be("outer");
            target.Inner.Should().NotBeNull().And.BeOfType<Inner>();
            target.Inner!.Value.Should().Be(5);
        }
    }

    public sealed class Outer
    {
        public string? Name { get; set; }

        public Inner? Inner { get; set; }
    }

    public sealed class Inner
    {
        public int Value { get; set; }
    }
}
