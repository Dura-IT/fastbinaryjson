using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Data;
using System.Dynamic;
using AwesomeAssertions;
using DuraIT.FastBinaryJson;
using DuraIT.FastBinaryJson.Internal;
using NUnit.Framework;

namespace FastBinaryJson.UnitTests.Internal
{
    [TestFixture]
    [TestOf(typeof(TypeReflector))]
    [NonParallelizable]
    internal sealed class TypeReflectorTests
    {
        [TearDown]
        public void RemoveRegistrations()
        {
            TypeReflector.Instance.ClearCustomTypes();
        }

        [TestCase(typeof(Ordinary), WriteKind.Object)]
        [TestCase(typeof(OrdinaryStruct), WriteKind.Object)]
        [TestCase(typeof(DataSet), WriteKind.DataSet)]
        [TestCase(typeof(DataTable), WriteKind.DataTable)]
        [TestCase(typeof(DayOfWeek), WriteKind.Enum)]
        [TestCase(typeof(DateTimeOffset), WriteKind.DateTimeOffset)]
        [TestCase(typeof(ExpandoObject), WriteKind.ExpandoDictionary)]
        [TestCase(typeof(StringDictionary), WriteKind.StringDictionary)]
        [TestCase(typeof(NameValueCollection), WriteKind.NameValueCollection)]
        [TestCase(typeof(List<int>), WriteKind.Sequence)]
        [TestCase(typeof(Dictionary<string, int>), WriteKind.StringKeyedDictionary)]
        [TestCase(typeof(Dictionary<int, int>), WriteKind.Dictionary)]
        [TestCase(typeof(Hashtable), WriteKind.Dictionary)]
        [TestCase(typeof(int[]), WriteKind.Array)]
        [TestCase(typeof(byte[]), WriteKind.Bytes)]
        public void GetWriteKind_Type_ClassifiesItTheWayWriteValueAlwaysDid(Type type, WriteKind expected)
        {
            TypeReflector.Instance.GetWriteKind(type).Should().Be(expected);
        }

        [Test]
        public void GetWriteKind_TypeRegisteredAfterBeingAsked_BecomesCustom()
        {
            TypeReflector.Instance.GetWriteKind(typeof(Ordinary)).Should().Be(WriteKind.Object);

            Bjson.RegisterCustomType(typeof(Ordinary), _ => string.Empty, _ => new Ordinary());

            TypeReflector.Instance.GetWriteKind(typeof(Ordinary)).Should().Be(WriteKind.Custom);
        }

        [Test]
        public void GetWriteKind_RegisteredTypeThatIsAlsoASequence_StaysASequence()
        {
            // The chain has always tested collections before custom registrations.
            Bjson.RegisterCustomType(typeof(List<string>), _ => string.Empty, _ => new List<string>());

            TypeReflector.Instance.GetWriteKind(typeof(List<string>)).Should().Be(WriteKind.Sequence);
        }

        [Test]
        public void ClearReflectionCache_AfterTypesWereAsked_DropsTheClassifications()
        {
            TypeReflector.Instance.GetWriteKind(typeof(Ordinary)).Should().Be(WriteKind.Object);
            TypeReflector.Instance.WriteKindCacheCount.Should().BeGreaterThan(0);

            TypeReflector.Instance.ClearReflectionCache();

            TypeReflector.Instance.WriteKindCacheCount.Should().Be(0);
        }

        [TestCase("System.Int32, System.Private.CoreLib, Version=10.0.0.0, Culture=neutral, PublicKeyToken=7cec85d7bea7798e", "System.Int32")]
        [TestCase(
            "System.Collections.Generic.List`1[[System.String, System.Private.CoreLib, Version=10.0.0.0, Culture=neutral, PublicKeyToken=7cec85d7bea7798e]], System.Private.CoreLib, Version=10.0.0.0, Culture=neutral, PublicKeyToken=7cec85d7bea7798e",
            "System.Collections.Generic.List`1[[System.String]]"
        )]
        [TestCase("MyApp.Thing, MyApp, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null", null)]
        public void WithoutCoreLibrary_Name_DropsOnlyTheCoreLibraryQualifier(string name, string? expected)
        {
            TypeReflector.WithoutCoreLibrary(name).Should().Be(expected);
        }

        public sealed class Ordinary
        {
            public int Value { get; set; }
        }

        public struct OrdinaryStruct
        {
            public int Value { get; set; }
        }
    }
}
