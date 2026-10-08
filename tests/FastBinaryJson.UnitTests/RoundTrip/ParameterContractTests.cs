using System;
using System.Collections.Generic;
using AwesomeAssertions;
using DuraIT.FastBinaryJson;
using NUnit.Framework;

namespace FastBinaryJson.UnitTests.RoundTrip
{
    /*
     * The parameters passed to a call are "for this call only". Upstream resolved conflicting settings
     * on the caller's own object (UseExtensions off switches global types off) and so changed the
     * caller's settings, including the shared Bjson.Parameters, for every later call.
     */
    [TestFixture]
    [TestOf(typeof(Bjson))]
    public sealed class ParameterContractTests
    {
        [Test]
        public void ToBjson_ConflictingParameters_LeavesTheCallersParametersAlone()
        {
            var parameters = new BjsonParameters { UseExtensions = false };

            Bjson.ToBjson(new Holder(), parameters);

            parameters.UsingGlobalTypes.Should().BeTrue();
        }

        [Test]
        public void ToObject_ConflictingParameters_LeavesTheCallersParametersAlone()
        {
            byte[] bytes = Bjson.ToBjson(new Holder());
            var parameters = new BjsonParameters { EnableAnonymousTypes = true };

            Bjson.ToObject(bytes, parameters);

            parameters.ShowReadOnlyProperties.Should().BeFalse();
        }

        [Test]
        public void MakeCopy_IgnoreAttributesChangedAfterwards_DoesNotReachTheCopy()
        {
            var parameters = new BjsonParameters();
            BjsonParameters copy = parameters.MakeCopy();

            parameters.IgnoreAttributes.Add(typeof(ObsoleteAttribute));

            copy.IgnoreAttributes.Should().NotContain(typeof(ObsoleteAttribute));
        }

        [Test]
        public void ToObjectGeneric_NullPayload_ReturnsTheDefault()
        {
            byte[] nullPayload = Bjson.ToBjson(null!);

            Bjson.ToObject<Holder>(nullPayload).Should().BeNull();
            Bjson.ToObject<int>(nullPayload).Should().Be(0);
            Bjson.ToObject<List<int>>(nullPayload).Should().BeNull();
            Type holderType = typeof(Holder);
            Bjson.ToObject(nullPayload, holderType).Should().BeNull();
        }

        [Test]
        public void ToObjectGeneric_EnableAnonymousTypes_ReadsReadOnlyProperties()
        {
            byte[] bytes = Bjson.ToBjson(new Holder { Items = { 1, 2 } }, new BjsonParameters { ShowReadOnlyProperties = true });

            Holder? read = Bjson.ToObject<Holder>(bytes, new BjsonParameters { EnableAnonymousTypes = true });

            read!.Items.Should().Equal(1, 2);
        }

        public sealed class Holder
        {
            public List<int> Items { get; } = new List<int>();
        }
    }
}
