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
