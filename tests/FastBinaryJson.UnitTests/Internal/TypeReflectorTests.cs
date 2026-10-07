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
    public sealed class TypeReflectorTests
    {
        [TearDown]
        public void RemoveRegistrations()
        {
            TypeReflector.Instance.ClearCustomTypes();
        }

        [TestCase(typeof(Ordinary), true)]
        [TestCase(typeof(OrdinaryStruct), true)]
        [TestCase(typeof(DataSet), false)]
        [TestCase(typeof(DataTable), false)]
        [TestCase(typeof(DayOfWeek), false)]
        [TestCase(typeof(DateTimeOffset), false)]
        [TestCase(typeof(ExpandoObject), false)]
        [TestCase(typeof(StringDictionary), false)]
        [TestCase(typeof(NameValueCollection), false)]
        [TestCase(typeof(List<int>), false)]
        [TestCase(typeof(Dictionary<string, int>), false)]
        [TestCase(typeof(Hashtable), false)]
        [TestCase(typeof(int[]), false)]
        public void IsPlainObject_Type_MatchesWhatWriteValueDoesWithIt(Type type, bool expected)
        {
            TypeReflector.Instance.IsPlainObject(type).Should().Be(expected);
        }

        [Test]
        public void IsPlainObject_TypeRegisteredAfterBeingAsked_NoLongerPlain()
        {
            TypeReflector.Instance.IsPlainObject(typeof(Ordinary)).Should().BeTrue();

            Bjson.RegisterCustomType(typeof(Ordinary), _ => string.Empty, _ => new Ordinary());

            TypeReflector.Instance.IsPlainObject(typeof(Ordinary)).Should().BeFalse();
        }

        [Test]
        public void ClearReflectionCache_AfterTypesWereAsked_DropsThePlainObjectAnswers()
        {
            TypeReflector.Instance.IsPlainObject(typeof(Ordinary)).Should().BeTrue();
            TypeReflector.Instance.PlainObjectCacheCount.Should().BeGreaterThan(0);

            TypeReflector.Instance.ClearReflectionCache();

            TypeReflector.Instance.PlainObjectCacheCount.Should().Be(0);
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
