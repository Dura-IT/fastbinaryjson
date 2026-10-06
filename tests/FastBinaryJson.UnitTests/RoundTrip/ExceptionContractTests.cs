using System;
using AwesomeAssertions;
using DuraIT.FastBinaryJson;
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
    [TestOf(typeof(BJSON))]
    public sealed class ExceptionContractTests
    {
        private const int DeeperThanTheSerializerAllows = 40;

        [Test]
        public void Parse_UnrecognizedToken_ThrowsBjsonException()
        {
            byte[] bytes = { 0xFE };

            FluentActions.Invoking(() => BJSON.Parse(bytes)).Should().Throw<BjsonException>();
        }

        [Test]
        public void ToBJSON_GraphDeeperThanTheLimit_ThrowsBjsonException()
        {
            var root = new Node();
            Node current = root;
            for (int i = 0; i < DeeperThanTheSerializerAllows; i++)
            {
                current.Child = new Node();
                current = current.Child;
            }

            FluentActions.Invoking(() => BJSON.ToBJSON(root)).Should().Throw<BjsonException>();
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
        public void ToBJSON_NullParameters_ThrowsArgumentNull()
        {
            FluentActions.Invoking(() => BJSON.ToBJSON(new Node(), null!)).Should().Throw<ArgumentNullException>().WithParameterName("param");
        }

        [Test]
        public void ToObject_NullParameters_ThrowsArgumentNull()
        {
            byte[] bytes = BJSON.ToBJSON(new Node());

            FluentActions.Invoking(() => BJSON.ToObject(bytes, (BJSONParameters)null!)).Should().Throw<ArgumentNullException>().WithParameterName("param");
        }

        [Test]
        public void ToObjectGeneric_NullParameters_ThrowsArgumentNull()
        {
            byte[] bytes = BJSON.ToBJSON(new Node());

            FluentActions.Invoking(() => BJSON.ToObject<Node>(bytes, null!)).Should().Throw<ArgumentNullException>().WithParameterName("param");
        }

        [Test]
        public void FillObject_NullInput_ThrowsArgumentNull()
        {
            byte[] bytes = BJSON.ToBJSON(new Node());

            FluentActions.Invoking(() => BJSON.FillObject(null!, bytes)).Should().Throw<ArgumentNullException>().WithParameterName("input");
        }

        public sealed class Node
        {
            public Node? Child { get; set; }
        }
    }
}
