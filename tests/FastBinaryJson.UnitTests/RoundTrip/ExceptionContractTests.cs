using System;
using AwesomeAssertions;
using DuraIT.FastBinaryJson;
using DuraIT.FastBinaryJson.Internal;
using NUnit.Framework;

namespace FastBinaryJson.UnitTests.RoundTrip
{
    /*
     * What a caller can catch. Upstream threw a bare System.Exception for every format failure, which
     * cannot be told apart from anything else going wrong; BjsonException is the one type for "this
     * payload or this object graph cannot be handled". It still derives from Exception, so a
     * handler written for the base type keeps working.
     */
    [TestFixture]
    [TestOf(typeof(Bjson))]
    public sealed class ExceptionContractTests
    {
        private const int DeeperThanTheSerializerAllows = 40;

        [Test]
        public void Parse_UnrecognizedToken_ThrowsBjsonException()
        {
            byte[] bytes = { 0xFE };

            FluentActions.Invoking(() => Bjson.Parse(bytes)).Should().Throw<BjsonException>();
        }

        [Test]
        public void ToBjson_GraphDeeperThanTheLimit_ThrowsBjsonException()
        {
            var root = new Node();
            Node current = root;
            for (int i = 0; i < DeeperThanTheSerializerAllows; i++)
            {
                current.Child = new Node();
                current = current.Child;
            }

            FluentActions.Invoking(() => Bjson.ToBjson(root)).Should().Throw<BjsonException>();
        }

        [Test]
        public void BjsonException_IsAnException()
        {
            var exception = new BjsonException("message", new InvalidOperationException());

            exception.Should().BeAssignableTo<Exception>();
            exception.Message.Should().Be("message");
            exception.InnerException.Should().BeOfType<InvalidOperationException>();
        }

        [Test]
        public void ToBjson_NullParameters_ThrowsArgumentNull()
        {
            FluentActions.Invoking(() => Bjson.ToBjson(new Node(), null!)).Should().Throw<ArgumentNullException>().WithParameterName("param");
        }

        [Test]
        public void ToObject_NullParameters_ThrowsArgumentNull()
        {
            byte[] bytes = Bjson.ToBjson(new Node());

            FluentActions.Invoking(() => Bjson.ToObject(bytes, (BjsonParameters)null!)).Should().Throw<ArgumentNullException>().WithParameterName("param");
        }

        [Test]
        public void ToObjectGeneric_NullParameters_ThrowsArgumentNull()
        {
            byte[] bytes = Bjson.ToBjson(new Node());

            FluentActions.Invoking(() => Bjson.ToObject<Node>(bytes, null!)).Should().Throw<ArgumentNullException>().WithParameterName("param");
        }

        [Test]
        public void FillObject_NullInput_ThrowsArgumentNull()
        {
            byte[] bytes = Bjson.ToBjson(new Node());

            FluentActions.Invoking(() => Bjson.FillObject(null!, bytes)).Should().Throw<ArgumentNullException>().WithParameterName("input");
        }

        [Test]
        public void TypeReflector_NullArguments_ThrowArgumentNull()
        {
            TypeReflector reflection = TypeReflector.Instance;

            FluentActions.Invoking(() => reflection.GetTypeAssemblyName(null!)).Should().Throw<ArgumentNullException>().WithParameterName("t");
            FluentActions.Invoking(() => reflection.GetGenericArguments(null!)).Should().Throw<ArgumentNullException>().WithParameterName("t");
            FluentActions.Invoking(() => reflection.GetGenericTypeDefinition(null!)).Should().Throw<ArgumentNullException>().WithParameterName("t");
            FluentActions.Invoking(() => reflection.Getproperties(null!, "name", false)).Should().Throw<ArgumentNullException>().WithParameterName("type");
            FluentActions.Invoking(() => TypeReflector.UnicodeGetBytes(null!)).Should().Throw<ArgumentNullException>().WithParameterName("str");
            FluentActions.Invoking(() => TypeReflector.UnicodeGetString(null!)).Should().Throw<ArgumentNullException>().WithParameterName("b");
        }

        public sealed class Node
        {
            public Node? Child { get; set; }
        }
    }
}
