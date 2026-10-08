using System;
using System.Collections.Generic;
using AwesomeAssertions;
using DuraIT.FastBinaryJson;
using NUnit.Framework;

namespace FastBinaryJson.UnitTests.RoundTrip
{
    /*
     * Reads that take a type or no type at all now also take the settings for the call, so a caller that
     * decides the type at run time does not have to change the global Bjson.Parameters to choose them.
     */
    [TestFixture]
    [TestOf(typeof(Bjson))]
    public sealed class PerCallParametersTests
    {
        public sealed class Stamped
        {
            public DateTime When { get; set; }

            public string? Name { get; set; }
        }

        [Test]
        public void ToObject_TypeAndParameters_ReadsTheType()
        {
            BjsonParameters parameters = new BjsonParameters { UseUtcDateTime = true };
            Stamped original = new Stamped { When = new DateTime(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc), Name = "n" };
            byte[] bytes = Bjson.ToBjson(original, parameters);

            object? back = Bjson.ToObject(bytes, typeof(Stamped), parameters);

            back.Should().BeOfType<Stamped>();
            ((Stamped)back!).Name.Should().Be("n");
            ((Stamped)back).When.Kind.Should().Be(DateTimeKind.Utc);
            ((Stamped)back).When.Should().Be(original.When);
        }

        [Test]
        public void ToObject_TypeAndParameters_UsesTheParametersOfTheCall()
        {
            byte[] bytes = Bjson.ToBjson(
                new Stamped { When = new DateTime(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc) },
                new BjsonParameters { UseUtcDateTime = true }
            );

            Stamped utc = (Stamped)Bjson.ToObject(bytes, typeof(Stamped), new BjsonParameters { UseUtcDateTime = true })!;
            Stamped notUtc = (Stamped)Bjson.ToObject(bytes, typeof(Stamped), new BjsonParameters { UseUtcDateTime = false })!;

            utc.When.Kind.Should().Be(DateTimeKind.Utc);
            notUtc.When.Kind.Should().NotBe(DateTimeKind.Utc);
        }

        [Test]
        public void ToObject_TypeAndParameters_NullArguments_Throw()
        {
            byte[] bytes = Bjson.ToBjson(new Stamped());

            ((Action)(() => Bjson.ToObject(null!, typeof(Stamped), new BjsonParameters()))).Should().Throw<ArgumentNullException>();
            ((Action)(() => Bjson.ToObject(bytes, null!, new BjsonParameters()))).Should().Throw<ArgumentNullException>();
            ((Action)(() => Bjson.ToObject(bytes, typeof(Stamped), null!))).Should().Throw<ArgumentNullException>();
        }

        [Test]
        public void ToObject_TypeAndParameters_CorruptBytes_ThrowBjsonException()
        {
            byte[] bytes = Bjson.ToBjson(new Stamped { Name = "name" });

            Action read = () => Bjson.ToObject(bytes.AsSpan(0, bytes.Length / 2).ToArray(), typeof(Stamped), new BjsonParameters());

            read.Should().Throw<BjsonException>();
        }

        [Test]
        public void Parse_WithParameters_ReturnsTheSameShapeAsParse()
        {
            byte[] bytes = Bjson.ToBjson(new Stamped { Name = "n" });

            object? withParameters = Bjson.Parse(bytes, new BjsonParameters());

            withParameters.Should().BeOfType<Dictionary<string, object>>();
            ((Dictionary<string, object>)withParameters!)["Name"].Should().Be("n");
            withParameters.Should().BeEquivalentTo(Bjson.Parse(bytes));
        }

        [Test]
        public void Parse_WithParameters_NullArguments_Throw()
        {
            byte[] bytes = Bjson.ToBjson(new Stamped());

            ((Action)(() => Bjson.Parse(null!, new BjsonParameters()))).Should().Throw<ArgumentNullException>();
            ((Action)(() => Bjson.Parse(bytes, null!))).Should().Throw<ArgumentNullException>();
        }

        [Test]
        public void Parse_WithParameters_CorruptBytes_ThrowBjsonException()
        {
            byte[] bytes = Bjson.ToBjson(new Stamped { Name = "name" });

            Action parse = () => Bjson.Parse(bytes.AsSpan(0, bytes.Length / 2).ToArray(), new BjsonParameters());

            parse.Should().Throw<BjsonException>();
        }
    }
}
