// ReSharper disable UnusedAutoPropertyAccessor.Global - reflection-only models: the serializer reads and writes these members, nothing calls them
using AwesomeAssertions;
using DuraIT.FastBinaryJson;
using DuraIT.FastBinaryJson.Internal;
using NUnit.Framework;

namespace FastBinaryJson.UnitTests.RoundTrip
{
    /*
     * WriteValue skips its chain of special cases for a type it has already found to be an ordinary object.
     * That answer must not outlive a custom registration for the type, or for one of its base types.
     */
    [TestFixture]
    [TestOf(typeof(Bjson))]
    [NonParallelizable]
    public sealed class PlainObjectCacheTests
    {
        [TearDown]
        public void RemoveRegistrations()
        {
            TypeReflector.Instance.ClearCustomTypes();
        }

        [Test]
        public void ToBjson_TypeRegisteredAfterBeingWrittenAsAnObject_UsesTheRegistration()
        {
            var holder = new Holder { Value = new Tagged { Text = "a" } };
            byte[] plain = Bjson.ToBjson(holder);

            Bjson.RegisterCustomType(typeof(Tagged), o => "custom:" + ((Tagged)o).Text, s => new Tagged { Text = s });
            byte[] registered = Bjson.ToBjson(holder);

            registered.Should().NotEqual(plain);
            Bjson.ToObject<Holder>(registered)!.Value!.Text.Should().Be("custom:a");
        }

        [Test]
        public void ToBjson_BaseTypeRegisteredAfterDerivedWasWrittenAsAnObject_UsesTheRegistration()
        {
            var holder = new DerivedHolder { Value = new DerivedTagged { Text = "b" } };
            byte[] plain = Bjson.ToBjson(holder);

            Bjson.RegisterCustomType(typeof(Tagged), o => "base:" + ((Tagged)o).Text, s => new DerivedTagged { Text = s });
            byte[] registered = Bjson.ToBjson(holder);

            registered.Should().NotEqual(plain);
            Bjson.ToObject<DerivedHolder>(registered)!.Value!.Text.Should().Be("base:b");
        }

        public class Tagged
        {
            public string? Text { get; set; }
        }

        public sealed class DerivedTagged : Tagged
        {
            public int Extra { get; set; }
        }

        public sealed class Holder
        {
            public Tagged? Value { get; set; }
        }

        public sealed class DerivedHolder
        {
            public DerivedTagged? Value { get; set; }
        }
    }
}
