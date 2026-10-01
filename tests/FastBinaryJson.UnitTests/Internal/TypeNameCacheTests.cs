using System;
using System.Collections.Generic;

using AwesomeAssertions;

using DuraIT.FastBinaryJson;
using DuraIT.FastBinaryJson.Internal;

using FastBinaryJson.UnitTests.RoundTrip;

using NUnit.Framework;

namespace FastBinaryJson.UnitTests.Internal
{
    /*
     * net10.0 only: the netstandard2.0 test project removes this file from its compile glob, because
     * the netstandard2.0 build of the library has no TypeNameCache and resolves $type the old way.
     *
     * The cache is process-wide, so these tests use names no other test uses and never assert on
     * how full it is.
     */
    [TestFixture]
    [TestOf(typeof(TypeNameCache))]
    public sealed class TypeNameCacheTests
    {
        [Test]
        public void Resolve_KnownName_ReturnsWhatGetTypeFromCacheReturns()
        {
            string name = typeof(EqLeft).AssemblyQualifiedName!;

            Type? first = TypeNameCache.Resolve(name.AsSpan(), null);
            Type? second = TypeNameCache.Resolve(name.AsSpan(), null);

            first.Should().Be(typeof(EqLeft));
            second.Should().Be(typeof(EqLeft));
        }

        [Test]
        public void Resolve_UnknownName_ReturnsNullEveryTime()
        {
            const string Name = "TypeNameCacheProbe.DoesNotExist, NoSuchAssembly";

            TypeNameCache.Resolve(Name.AsSpan(), null).Should().BeNull();
            TypeNameCache.Resolve(Name.AsSpan(), null).Should().BeNull();
        }

        [Test]
        public void Resolve_DenylistedName_ThrowsEveryTime()
        {
            const string Name = "System.Windows.Data.ObjectDataProvider, TypeNameCacheProbe";

            FluentActions.Invoking(() => TypeNameCache.Resolve(Name.AsSpan(), null)).Should().Throw<Exception>().WithMessage("Black list type*");
            FluentActions.Invoking(() => TypeNameCache.Resolve(Name.AsSpan(), null)).Should().Throw<Exception>().WithMessage("Black list type*");
        }

        [Test]
        public void Resolve_NameInGlobalTypes_UsesItsEntry()
        {
            Dictionary<string, object> globaltypes = new Dictionary<string, object> { ["7"] = typeof(EqRight).AssemblyQualifiedName! };

            TypeNameCache.Resolve("7".AsSpan(), globaltypes).Should().Be(typeof(EqRight));
        }

        [Test]
        public void Resolve_GlobalTypesEntryNotAString_ThrowsLikeResolveType()
        {
            Dictionary<string, object> globaltypes = new Dictionary<string, object> { ["8"] = 8 };

            FluentActions.Invoking(() => TypeNameCache.Resolve("8".AsSpan(), globaltypes)).Should().Throw<InvalidCastException>();
            FluentActions.Invoking(() => Deserializer.ResolveType("8", globaltypes)).Should().Throw<InvalidCastException>();
        }

        /// <summary>
        /// A root List is written without global types, so each element carries its own $type name -
        /// the case this cache exists for. Every one of them must be resolved in place.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        public void ToObject_RootListElements_ResolveTypeInPlace(bool unicode)
        {
            BJSONParameters parameters = new BJSONParameters { UseUnicodeStrings = unicode };
            List<EqBase> value = new List<EqBase> { new EqLeft { Left = 1 }, new EqRight { Right = 2 }, new EqLeft { Left = 3 } };
            Deserializer deserializer = new Deserializer(parameters);

            List<EqBase> restored = (List<EqBase>)deserializer.ToObject(BJSON.ToBJSON(value, parameters), typeof(List<EqBase>))!;

            restored.Should().BeEquivalentTo(value, o => o.PreferringRuntimeMemberTypes().WithStrictOrdering());
            deserializer.TypesResolvedInPlace.Should().Be(3);
        }
    }
}
