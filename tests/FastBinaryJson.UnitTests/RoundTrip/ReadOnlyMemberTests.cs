using AwesomeAssertions;
using DuraIT.FastBinaryJson;
using NUnit.Framework;

namespace FastBinaryJson.UnitTests.RoundTrip
{
    /*
     * Members a type cannot assign from outside: readonly fields, get-only auto-properties, private setters.
     * The serializer writes them all. ShowReadOnlyProperties decides whether the reader puts them back:
     * it already covered get-only auto-properties and non-public setters, and a readonly field used to be
     * skipped even with it on, so a value that was written came back as the default with no error.
     *
     * Every test has its own model type because the member metadata is cached per type for the process.
     */
    [TestFixture]
    [TestOf(typeof(Bjson))]
    public sealed class ReadOnlyMemberTests
    {
        public sealed class WithReadOnlyField
        {
            public readonly int Id;
            public readonly string? Name;
            public int Mutable;

            public WithReadOnlyField() { }

            public WithReadOnlyField(int id, string name)
            {
                Id = id;
                Name = name;
            }
        }

        public sealed class ReadOnlyFirstThenDefault
        {
            public string? GetOnly { get; }

            public ReadOnlyFirstThenDefault() { }

            public ReadOnlyFirstThenDefault(string getOnly)
            {
                GetOnly = getOnly;
            }
        }

        public sealed class DefaultFirstThenReadOnly
        {
            public string? GetOnly { get; }

            public DefaultFirstThenReadOnly() { }

            public DefaultFirstThenReadOnly(string getOnly)
            {
                GetOnly = getOnly;
            }
        }

        [Test]
        public void ToObject_ShowReadOnlyProperties_RestoresReadOnlyFields()
        {
            BjsonParameters parameters = new BjsonParameters { ShowReadOnlyProperties = true };
            byte[] bytes = Bjson.ToBjson(new WithReadOnlyField(7, "seven") { Mutable = 5 }, parameters);

            WithReadOnlyField back = Bjson.ToObject<WithReadOnlyField>(bytes, parameters)!;

            back.Id.Should().Be(7);
            back.Name.Should().Be("seven");
            back.Mutable.Should().Be(5);
        }

        [Test]
        public void ToObject_ReadOnlyCallFirstThenDefaultCall_DefaultCallDoesNotRestoreGetOnly()
        {
            BjsonParameters show = new BjsonParameters { ShowReadOnlyProperties = true };
            byte[] bytes = Bjson.ToBjson(new ReadOnlyFirstThenDefault("value"), show);

            Bjson.ToObject<ReadOnlyFirstThenDefault>(bytes, show)!.GetOnly.Should().Be("value");
            Bjson.ToObject<ReadOnlyFirstThenDefault>(bytes, new BjsonParameters())!.GetOnly.Should().BeNull();
        }

        [Test]
        public void ToObject_DefaultCallFirstThenReadOnlyCall_ReadOnlyCallRestoresGetOnly()
        {
            BjsonParameters show = new BjsonParameters { ShowReadOnlyProperties = true };
            byte[] bytes = Bjson.ToBjson(new DefaultFirstThenReadOnly("value"), show);

            Bjson.ToObject<DefaultFirstThenReadOnly>(bytes, new BjsonParameters())!.GetOnly.Should().BeNull();
            Bjson.ToObject<DefaultFirstThenReadOnly>(bytes, show)!.GetOnly.Should().Be("value");
        }
    }
}
