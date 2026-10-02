using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

using AwesomeAssertions;

using DuraIT.FastBinaryJson;

using NUnit.Framework;

namespace FastBinaryJson.UnitTests.RoundTrip
{
    /*
     * The writer reads a primitive member through a typed getter and writes it without boxing. That
     * must produce exactly the bytes the boxing path produces, so every case writes the same value
     * both ways and compares. The subject mixes everything the typed path takes (each primitive as a
     * property and as a field, an inherited member, extremes) with everything it must leave alone
     * (nullables, an enum, a string, DateTimeOffset, a struct, a static).
     */
    [TestFixture]
    [TestOf(typeof(BJSONSerializer))]
    public sealed class TypedGetterTests
    {
        internal static IEnumerable<TestCaseData> ParameterSets()
        {
            yield return new TestCaseData(new BJSONParameters()).SetArgDisplayNames("defaults");
            yield return new TestCaseData(new BJSONParameters { UseUnicodeStrings = false }).SetArgDisplayNames("utf8");
            yield return new TestCaseData(new BJSONParameters { UseUTCDateTime = true }).SetArgDisplayNames("utc");
            yield return new TestCaseData(new BJSONParameters { UseExtensions = false, UsingGlobalTypes = false }).SetArgDisplayNames("no-extensions");
            yield return new TestCaseData(new BJSONParameters { SerializeNulls = true }).SetArgDisplayNames("nulls-serialized");
        }

        [TestCaseSource(nameof(ParameterSets))]
        public void ToBJSON_Primitives_WriteTheBoxingPathsBytes(BJSONParameters parameters)
        {
            TypedGetterSubject value = TypedGetterSubject.Create();

            Write(value, parameters, typed: true).Should().Equal(Write(value, parameters, typed: false));
        }

        [TestCaseSource(nameof(ParameterSets))]
        public void ToBJSON_ExtremeValues_WriteTheBoxingPathsBytes(BJSONParameters parameters)
        {
            TypedGetterSubject value = TypedGetterSubject.CreateExtremes();

            Write(value, parameters, typed: true).Should().Equal(Write(value, parameters, typed: false));
        }

        [Test]
        public void ToBJSON_Primitives_RoundTrip()
        {
            TypedGetterSubject value = TypedGetterSubject.Create();

            TypedGetterSubject restored = BJSON.ToObject<TypedGetterSubject>(BJSON.ToBJSON(value))!;

            // ReadOnlyInt is written - the byte comparisons cover it - but a private setter is only
            // read back with ShowReadOnlyProperties, which is off by default.
            restored.Should().BeEquivalentTo(value, o => o.IncludingFields().Excluding(s => s.ReadOnlyInt));
        }

        private static byte[] Write(object value, BJSONParameters parameters, bool typed)
        {
            parameters.FixValues();
            using (BJSONSerializer serializer = new BJSONSerializer(parameters.MakeCopy()) { TypedGetters = typed })
                return serializer.ConvertToBJSON(value);
        }
    }

    public class TypedGetterBase
    {
        public long Inherited { get; set; }
    }

    public sealed class TypedGetterSubject : TypedGetterBase
    {
        public static int StaticValue { get; set; } = 99;

        public int IntValue { get; set; }

        public long LongValue { get; set; }

        public bool BoolTrue { get; set; }

        public bool BoolFalse { get; set; }

        public DateTime Local { get; set; }

        public DateTime Utc { get; set; }

        public Guid Id { get; set; }

        public double DoubleValue { get; set; }

        public float FloatValue { get; set; }

        public decimal DecimalValue { get; set; }

        public short ShortValue { get; set; }

        public ushort UShortValue { get; set; }

        public uint UIntValue { get; set; }

        public ulong ULongValue { get; set; }

        public byte ByteValue { get; set; }

        public sbyte SByteValue { get; set; }

        public char CharValue { get; set; }

        public TimeSpan Span { get; set; }

        [DataMember(Name = "renamed")]
        public int Renamed { get; set; }

        public int ReadOnlyInt { get; private set; }

        public int? NullableSet { get; set; }

        public int? NullableEmpty { get; set; }

        public TypedGetterKind Kind { get; set; }

        public string? Text { get; set; }

        public DateTimeOffset Offset { get; set; }

        public TypedGetterPoint Point { get; set; }

        public int IntField;

        public double DoubleField;

        public char CharField;

        public static TypedGetterSubject Create()
        {
            return new TypedGetterSubject
            {
                Inherited = 41,
                IntValue = -7,
                LongValue = 1L << 40,
                BoolTrue = true,
                BoolFalse = false,
                Local = new DateTime(2026, 10, 2, 13, 14, 15, DateTimeKind.Local),
                Utc = new DateTime(2026, 10, 2, 11, 14, 15, DateTimeKind.Utc),
                Id = new Guid("0f8fad5b-d9cb-469f-a165-70867728950e"),
                DoubleValue = Math.PI,
                FloatValue = 1.5f,
                DecimalValue = 12345.6789m,
                ShortValue = -300,
                UShortValue = 60000,
                UIntValue = 4000000000,
                ULongValue = ulong.MaxValue - 1,
                ByteValue = 200,
                SByteValue = -42,
                CharValue = 'é',
                Span = TimeSpan.FromMinutes(90),
                Renamed = 5,
                ReadOnlyInt = 6,
                NullableSet = 8,
                NullableEmpty = null,
                Kind = TypedGetterKind.Second,
                Text = "text",
                Offset = new DateTimeOffset(2026, 10, 2, 13, 14, 15, TimeSpan.FromHours(2)),
                Point = new TypedGetterPoint { X = 1, Y = 2 },
                IntField = 9,
                DoubleField = -0.5,
                CharField = 'z',
            };
        }

        public static TypedGetterSubject CreateExtremes()
        {
            return new TypedGetterSubject
            {
                Inherited = long.MinValue,
                IntValue = int.MinValue,
                LongValue = long.MaxValue,
                Local = DateTime.MinValue,
                Utc = DateTime.MaxValue,
                Id = Guid.Empty,
                DoubleValue = double.NaN,
                FloatValue = float.NegativeInfinity,
                DecimalValue = decimal.MinValue,
                ShortValue = short.MinValue,
                UShortValue = ushort.MaxValue,
                UIntValue = uint.MaxValue,
                ULongValue = ulong.MaxValue,
                ByteValue = byte.MaxValue,
                SByteValue = sbyte.MinValue,
                CharValue = char.MaxValue,
                Span = TimeSpan.MinValue,
                IntField = int.MaxValue,
                DoubleField = double.Epsilon,
                CharField = '\0',
            };
        }
    }

    public enum TypedGetterKind
    {
        First,
        Second,
    }

    public struct TypedGetterPoint
    {
        public int X { get; set; }

        public int Y { get; set; }
    }
}
