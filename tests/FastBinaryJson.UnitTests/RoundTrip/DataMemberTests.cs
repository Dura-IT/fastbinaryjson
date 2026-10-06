// ReSharper disable UnusedAutoPropertyAccessor.Global - reflection-only models: the serializer reads and writes these members, nothing calls them
using System.Collections.Generic;
using System.Runtime.Serialization;
using AwesomeAssertions;
using DuraIT.FastBinaryJson;
using NUnit.Framework;

namespace FastBinaryJson.UnitTests.RoundTrip
{
    /*
     * [DataMember(Name = ...)] arrived in upstream v1.4.23 (25058e9) with the Reflection.cs it shares
     * with fastJSON. The reader half came along - Getproperties keys such a member under its
     * DataMember name, exactly as written - but the writer half never did: WriteObject writes
     * Getters.Name, the C# name, and ignores memberName. The reader looks keys up lowercased, so it
     * searches for the lowercased C# name, finds nothing, and silently drops the value.
     *
     * The one case that worked was a DataMember name that already equals the lowercased C# name.
     *
     * Fixed by finishing the feature: the writer writes the DataMember name, and the reader accepts
     * it case-insensitively like every other key. It also still accepts the C# name, because every
     * stream written before this fork carries that - those must keep loading.
     */
    [TestFixture]
    [TestOf(typeof(Bjson))]
    public sealed class DataMemberTests
    {
        [Test]
        public void ToObject_PropertyWithDataMemberName_IsRestored()
        {
            AliasedProperty restored = Bjson.ToObject<AliasedProperty>(Bjson.ToBjson(new AliasedProperty { Value = "kept", Plain = 7 }))!;

            restored.Plain.Should().Be(7, "the member without a DataMember name is the control");
            restored.Value.Should().Be("kept");
        }

        [Test]
        public void ToObject_FieldWithDataMemberName_IsRestored()
        {
            AliasedField restored = Bjson.ToObject<AliasedField>(Bjson.ToBjson(new AliasedField { Value = 42 }))!;

            restored.Value.Should().Be(42);
        }

        [Test]
        public void ToBjson_PropertyWithDataMemberName_WritesTheDataMemberName()
        {
            byte[] bytes = Bjson.ToBjson(new AliasedProperty { Value = "kept", Plain = 7 }, new BjsonParameters { UseExtensions = false });

            Bjson.Parse(bytes).Should().BeAssignableTo<Dictionary<string, object>>().Which.Keys.Should().BeEquivalentTo("Alias", "Plain");
        }

        /// <summary>
        /// A stream from before this fork carries the C# name, since no writer used DataMember names.
        /// It is produced here from a class with the same members and no attribute.
        /// </summary>
        [Test]
        public void ToObject_StreamWrittenWithCSharpName_StillRestoresTheMember()
        {
            BjsonParameters parameters = new BjsonParameters { UseExtensions = false };
            byte[] legacy = Bjson.ToBjson(new UnaliasedProperty { Value = "kept", Plain = 7 }, parameters);

            AliasedProperty restored = Bjson.ToObject<AliasedProperty>(legacy, parameters)!;

            restored.Value.Should().Be("kept");
            restored.Plain.Should().Be(7);
        }

        [Test]
        public void ToObject_DataMemberNameEqualToLowercasedName_IsRestored()
        {
            LowercaseAlias restored = Bjson.ToObject<LowercaseAlias>(Bjson.ToBjson(new LowercaseAlias { Count = 3 }))!;

            restored.Count.Should().Be(3);
        }
    }

    public sealed class AliasedProperty
    {
        [DataMember(Name = "Alias")]
        public string? Value { get; set; }

        public int Plain { get; set; }
    }

    public sealed class UnaliasedProperty
    {
        public string? Value { get; set; }

        public int Plain { get; set; }
    }

    public sealed class AliasedField
    {
        [DataMember(Name = "renamed")]
        public int Value;
    }

    public sealed class LowercaseAlias
    {
        [DataMember(Name = "count")]
        public int Count { get; set; }
    }
}
