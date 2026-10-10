using System;
using System.Collections.Generic;
using AwesomeAssertions;
using DuraIT.FastBinaryJson;
using NUnit.Framework;

namespace FastBinaryJson.UnitTests.RoundTrip
{
    /*
     * A read that takes no parameters uses Bjson.Parameters and must behave exactly like a read that is
     * handed those same parameters. It did not: the explicit-parameter overloads resolved conflicting
     * settings (EnableAnonymousTypes implies ShowReadOnlyProperties) and the global ones skipped that step.
     * Bjson.Parameters is process-wide, so this fixture cannot run next to the others.
     */
    [TestFixture]
    [TestOf(typeof(Bjson))]
    [NonParallelizable]
    public sealed class GlobalParametersTests
    {
        private BjsonParameters _saved = new BjsonParameters();

        [SetUp]
        public void SetUp()
        {
            _saved = Bjson.Parameters;
            Bjson.Parameters = new BjsonParameters { EnableAnonymousTypes = true };
        }

        [TearDown]
        public void TearDown()
        {
            Bjson.Parameters = _saved;
        }

        [Test]
        public void ToObject_TypeWithoutParameters_ResolvesConflictingGlobalSettings()
        {
            byte[] bytes = WriteHolder();

            Type holderType = typeof(Holder);

            Holder? read = (Holder?)Bjson.ToObject(bytes, holderType);

            read!.Items.Should().Equal(1, 2);
        }

        [Test]
        public void ToObjectGeneric_WithoutParameters_ResolvesConflictingGlobalSettings()
        {
            byte[] bytes = WriteHolder();

            Holder? read = Bjson.ToObject<Holder>(bytes);

            read!.Items.Should().Equal(1, 2);
        }

        [Test]
        public void ToObject_WithoutTypeOrParameters_ResolvesConflictingGlobalSettings()
        {
            byte[] bytes = WriteHolder();

            object? read = Bjson.ToObject(bytes);

            read.Should().BeOfType<Holder>().Which.Items.Should().Equal(1, 2);
        }

        [Test]
        public void FillObject_WithoutParameters_ResolvesConflictingGlobalSettings()
        {
            byte[] bytes = WriteHolder();
            Holder target = new Holder();

            Bjson.FillObject(target, bytes);

            target.Items.Should().Equal(1, 2);
        }

        private static byte[] WriteHolder() => Bjson.ToBjson(new Holder { Items = { 1, 2 } }, new BjsonParameters { ShowReadOnlyProperties = true });

        public sealed class Holder
        {
            public List<int> Items { get; } = new List<int>();
        }
    }
}
