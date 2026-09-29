using System.Net;

using AwesomeAssertions;

using DuraIT.FastBinaryJson;
using DuraIT.FastBinaryJson.Internal;

using NUnit.Framework;

namespace FastBinaryJson.UnitTests.RoundTrip
{
    /*
     * Custom type registration, which upstream applied to the exact registered type only.
     *
     * IsTypeRegistered did a dictionary lookup on obj.GetType(), so a registration for a base type
     * was skipped for every derived instance and serialization fell through to reflection. That was
     * not academic: IPAddress.Loopback on .NET returns the private subclass
     * System.Net.IPAddress+ReadOnlyIPAddress, so the most obvious way to obtain an IPAddress missed
     * a registration for typeof(IPAddress), and reflection then reached IPAddress.ScopeId, which
     * throws SocketException for any IPv4 address. On .NET Framework 4.0, where this was written,
     * Loopback was a plain IPAddress and the exact match held.
     *
     * Resolution now walks the base chain and takes the nearest registration. Interfaces are NOT
     * walked: a type can implement several registered interfaces with no defensible way to pick
     * between them, and upstream's exact match never covered them either.
     */
    [TestFixture]
    [TestOf(typeof(BJSON))]
    public sealed class CustomTypeTests
    {
        [TearDown]
        public void TearDown()
        {
            Reflection.Instance.ClearCustomTypes();
        }

        [Test]
        public void CustomType_AppliesToAFrameworkSubclassInstance()
        {
            BJSON.RegisterCustomType(typeof(IPAddress), x => x.ToString()!, x => IPAddress.Parse(x));

            IPAddress.Loopback.GetType().Should().NotBe(typeof(IPAddress), "Loopback is a private ReadOnlyIPAddress subclass on .NET");

            AddressHolder restored = BJSON.ToObject<AddressHolder>(BJSON.ToBJSON(new AddressHolder { Value = IPAddress.Loopback }))!;

            restored.Value.Should().Be(IPAddress.Loopback);
        }

        [Test]
        public void CustomType_AppliesToAUserDefinedSubclass()
        {
            BJSON.RegisterCustomType(typeof(Animal), x => "animal:" + ((Animal)x).Name, x => new Animal { Name = Suffix(x) });

            AnimalHolder restored = BJSON.ToObject<AnimalHolder>(BJSON.ToBJSON(new AnimalHolder { Value = new Dog { Name = "rex" } }))!;

            restored.Value.Name.Should().Be("rex", "the registration for the base type serialized the derived instance");
        }

        /// <summary>
        /// The nearest registration wins, not the first one found anywhere in the chain.
        /// </summary>
        [Test]
        public void CustomType_NearestRegistrationWins()
        {
            BJSON.RegisterCustomType(typeof(Animal), x => "animal:" + ((Animal)x).Name, x => new Animal { Name = Suffix(x) });
            BJSON.RegisterCustomType(typeof(Dog), x => "dog:" + ((Dog)x).Name, x => new Dog { Name = Suffix(x) });

            byte[] bytes = BJSON.ToBJSON(new Dog { Name = "rex" }, new BJSONParameters { UseExtensions = false });

            BJSON.Parse(bytes).Should().Be("dog:rex");
        }

        /// <summary>
        /// A type with no registration anywhere in its chain is still written by reflection.
        /// </summary>
        /// <remarks>
        /// Walking the base chain must not turn an unrelated type into a custom one. Every class
        /// derives from object, so a resolver that accepted a registration for a base type without
        /// checking which base would hand everything to whatever was registered first.
        /// </remarks>
        [Test]
        public void CustomType_UnrelatedType_IsStillWrittenByReflection()
        {
            BJSON.RegisterCustomType(typeof(Animal), x => "animal:" + ((Animal)x).Name, x => new Animal { Name = Suffix(x) });

            AnimalHolder restored = BJSON.ToObject<AnimalHolder>(BJSON.ToBJSON(new AnimalHolder { Value = new Dog { Name = "rex" } }))!;
            PartyHolder party = BJSON.ToObject<PartyHolder>(BJSON.ToBJSON(new PartyHolder { Name = "Acme" }))!;

            restored.Value.Name.Should().Be("rex");
            party.Name.Should().Be("Acme", "an unregistered type must not be routed through the Animal serializer");
        }

        private static string Suffix(string value)
        {
            return value.Substring(value.IndexOf(':') + 1);
        }

        private class Animal
        {
            public string Name { get; set; } = null!;
        }

        private sealed class Dog : Animal
        {
        }

        private sealed class AnimalHolder
        {
            public Animal Value { get; set; } = null!;
        }

        private sealed class AddressHolder
        {
            public IPAddress Value { get; set; } = null!;
        }

        private sealed class PartyHolder
        {
            public string Name { get; set; } = null!;
        }
    }
}
