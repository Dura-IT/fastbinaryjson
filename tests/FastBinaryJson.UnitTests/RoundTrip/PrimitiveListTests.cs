// ReSharper disable UnusedAutoPropertyAccessor.Global - reflection-only model: the serializer reads these members, nothing calls them
using System;
using System.Collections.Generic;
using AwesomeAssertions;
using DuraIT.FastBinaryJson;
using DuraIT.FastBinaryJson.Internal;
using NUnit.Framework;

namespace FastBinaryJson.UnitTests.RoundTrip
{
    /*
     * A List<T> or T[] of a primitive used to box every element on the way out, because the writer walked it
     * as a non-generic IEnumerable. It now writes each element straight from the typed collection. That must
     * produce exactly the bytes the boxing walk produces, so every sample is written both ways and compared,
     * under each parameter set and with typed arrays on and off.
     */
    [TestFixture]
    [TestOf(typeof(BjsonSerializer))]
    public sealed class PrimitiveListTests
    {
        private enum Color
        {
            Red,
            Green,
        }

        internal static IEnumerable<TestCaseData> ParameterSets()
        {
            foreach (TestCaseData set in TypedGetterTests.ParameterSets())
                yield return set;
            yield return new TestCaseData(new BjsonParameters { UseTypedArrays = false }).SetArgDisplayNames("no-typed-arrays");
            yield return new TestCaseData(new BjsonParameters { UseTypedArrays = false, UseUnicodeStrings = false }).SetArgDisplayNames("no-typed-arrays-utf8");
            yield return new TestCaseData(new BjsonParameters { UseV14TypedArray = true }).SetArgDisplayNames("v14-typed-arrays");
        }

        private static IEnumerable<object> Samples()
        {
            yield return new List<int> { int.MinValue, -1, 0, 1, int.MaxValue };
            yield return new[] { int.MinValue, -1, 0, 1, int.MaxValue };
            yield return new List<uint> { 0, 1, uint.MaxValue };
            yield return new[] { 0u, 1u, uint.MaxValue };
            yield return new List<long> { long.MinValue, -1, 0, long.MaxValue };
            yield return new[] { long.MinValue, -1, 0, long.MaxValue };
            yield return new List<ulong> { 0, 1, ulong.MaxValue };
            yield return new[] { 0ul, 1ul, ulong.MaxValue };
            yield return new List<short> { short.MinValue, 0, short.MaxValue };
            yield return new[] { short.MinValue, (short)0, short.MaxValue };
            yield return new List<ushort> { 0, 1, ushort.MaxValue };
            yield return new[] { (ushort)0, (ushort)1, ushort.MaxValue };
            yield return new List<byte> { 0, 1, byte.MaxValue };
            yield return new List<sbyte> { sbyte.MinValue, 0, sbyte.MaxValue };
            yield return new[] { sbyte.MinValue, (sbyte)0, sbyte.MaxValue };
            yield return new List<bool> { true, false, true };
            yield return new[] { true, false, true };
            yield return new List<char> { 'a', '\0', 'é', '￿' };
            yield return new[] { 'a', '\0', 'é', '￿' };
            yield return new List<float> { float.MinValue, -0f, 0.1f, float.NaN, float.PositiveInfinity };
            yield return new[] { float.MinValue, -0f, 0.1f, float.NaN, float.PositiveInfinity };
            yield return new List<double> { double.MinValue, -0d, 0.1d, double.NaN, double.NegativeInfinity };
            yield return new[] { double.MinValue, -0d, 0.1d, double.NaN, double.NegativeInfinity };
            yield return new List<decimal> { 0m, -1.5m, 100.00m, 1.2345678901234567890123456789m, decimal.MinValue, decimal.MaxValue };
            yield return new[] { 0m, -1.5m, 100.00m, 1.2345678901234567890123456789m, decimal.MinValue, decimal.MaxValue };
            yield return new List<Guid> { Guid.Empty, new Guid("0f8fad5b-d9cb-469f-a165-70867728950e"), Guid.NewGuid() };
            yield return new[] { Guid.Empty, new Guid("0f8fad5b-d9cb-469f-a165-70867728950e") };
            yield return new List<TimeSpan> { TimeSpan.Zero, TimeSpan.MinValue, TimeSpan.MaxValue, TimeSpan.FromMilliseconds(1234.5) };
            yield return new[] { TimeSpan.Zero, TimeSpan.FromDays(3) };
            yield return new List<DateTime>
            {
                DateTime.MinValue,
                DateTime.MaxValue,
                new DateTime(2026, 10, 12, 9, 30, 0, DateTimeKind.Utc),
                new DateTime(2026, 10, 12, 9, 30, 0, DateTimeKind.Local),
                new DateTime(2026, 10, 12, 9, 30, 0, DateTimeKind.Unspecified),
            };
            yield return new[] { new DateTime(2026, 10, 12, 9, 30, 0, DateTimeKind.Utc), new DateTime(2026, 10, 12, 9, 30, 0, DateTimeKind.Local) };

            // The ones the primitive path must leave alone, written the same either way.
            yield return new List<int>();
            yield return Array.Empty<int>();
            yield return new List<int?> { 1, null, 3 };
            yield return new List<string?> { "a", null, "c" };
            yield return new List<object?> { 1, 2L, "x", 3.5m, null };
            yield return new List<Color> { Color.Green, Color.Red };
            yield return new[] { Color.Green };
            yield return new List<long[]> { new long[] { 1, 2 }, new long[] { 3 } };
            yield return new List<List<decimal>>
            {
                new List<decimal> { 1m, 2m },
                new List<decimal>(),
            };
            yield return new Dictionary<string, List<int>>
            {
                ["a"] = new List<int> { 1, 2 },
            };
        }

        [TestCaseSource(nameof(ParameterSets))]
        public void ToBjson_PrimitiveCollections_WriteTheBoxingPathsBytes(BjsonParameters parameters)
        {
            Guard.NotNull(parameters, nameof(parameters));

            foreach (object sample in Samples())
                Write(sample, parameters, primitive: true)
                    .Should()
                    .Equal(Write(sample, parameters, primitive: false), "{0} must write the same bytes", sample.GetType());
        }

        [TestCaseSource(nameof(ParameterSets))]
        public void ToBjson_PrimitiveCollectionAsMember_WritesTheBoxingPathsBytes(BjsonParameters parameters)
        {
            Guard.NotNull(parameters, nameof(parameters));
            ListHolder holder = new ListHolder
            {
                Ints = new List<int> { 1, 2, 3 },
                Decimals = new[] { 1.5m, 2.5m },
                Guids = new List<Guid> { Guid.Empty },
            };

            Write(holder, parameters, primitive: true).Should().Equal(Write(holder, parameters, primitive: false));
        }

        [Test]
        public void ToBjson_ListOfDecimal_RoundTrips()
        {
            List<decimal> original = new List<decimal> { 0m, -1.5m, 100.00m, decimal.MinValue, decimal.MaxValue };

            Bjson.ToObject<List<decimal>>(Bjson.ToBjson(original)).Should().Equal(original);
        }

        [Test]
        public void ToBjson_ListOfDecimal_DoesNotBoxEachElement()
        {
            List<decimal> decimals = new List<decimal>();
            for (int i = 0; i < 1000; i++)
                decimals.Add(i + 0.5m);

            long boxing = Allocated(decimals, primitive: false);
            long direct = Allocated(decimals, primitive: true);

            // A boxed decimal is 32 bytes; anything near half of that saved per element proves the box is gone.
            (boxing - direct)
                .Should()
                .BeGreaterThan(16 * decimals.Count, "the primitive path boxed {0} bytes less than the boxing one", boxing - direct);
        }

        [Test]
        public void ToBjson_ListOfInt_DoesNotBoxEachElement()
        {
            List<int> ints = new List<int>();
            for (int i = 0; i < 1000; i++)
                ints.Add(i);

            long boxing = Allocated(ints, primitive: false);
            long direct = Allocated(ints, primitive: true);

            (boxing - direct).Should().BeGreaterThan(12 * ints.Count, "the primitive path boxed {0} bytes less than the boxing one", boxing - direct);
        }

        private static long Allocated(object value, bool primitive)
        {
            BjsonParameters parameters = new BjsonParameters();
            return AllocationProbe.PerCall(() => Write(value, parameters, primitive));
        }

        private static byte[] Write(object value, BjsonParameters parameters, bool primitive)
        {
            parameters.FixValues();
            using (BjsonSerializer serializer = new BjsonSerializer(parameters.MakeCopy()) { PrimitiveElements = primitive })
                return serializer.ConvertToBjson(value);
        }

        public sealed class ListHolder
        {
            public List<int>? Ints { get; set; }

            public decimal[]? Decimals { get; set; }

            public List<Guid>? Guids { get; set; }
        }
    }
}
