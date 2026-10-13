// ReSharper disable UnusedAutoPropertyAccessor.Global - reflection-only models: the serializer reads and writes these members, nothing calls them
// ReSharper disable CollectionNeverQueried.Global - the same models: their lists are filled by the reader and compared as a whole
using System;
using System.Collections;
using System.Collections.Generic;
using AwesomeAssertions;
using DuraIT.FastBinaryJson;
using DuraIT.FastBinaryJson.Internal;
using NUnit.Framework;

namespace FastBinaryJson.UnitTests.RoundTrip
{
    /*
     * A List<T> of a primitive T used to be read element by element into a List<object>, boxing each one, and
     * then copied into the real list. When every element carries T's own token it is now read straight into
     * a List<T>. Anything else - another width, a null, a mixed list, damaged bytes - is rewound and read the
     * old way, so the two paths must agree on every input: the same list, or both failing.
     */
    [TestFixture]
    [TestOf(typeof(TypedReader))]
    public sealed class PrimitiveListReadTests
    {
        // No $type in the payload, so the reader builds the type it is asked for and meets the elements as written.
        private static readonly BjsonParameters Untyped = new BjsonParameters { UseExtensions = false, UsingGlobalTypes = false };

        private static IEnumerable<IList> Lists()
        {
            yield return new List<int> { int.MinValue, -1, 0, 1, int.MaxValue };
            yield return new List<uint> { 0, 1, uint.MaxValue };
            yield return new List<long> { long.MinValue, -1, 0, long.MaxValue };
            yield return new List<ulong> { 0, 1, ulong.MaxValue };
            yield return new List<short> { short.MinValue, 0, short.MaxValue };
            yield return new List<ushort> { 0, 1, ushort.MaxValue };
            yield return new List<byte> { 0, 1, byte.MaxValue };
            yield return new List<sbyte> { sbyte.MinValue, 0, sbyte.MaxValue };
            yield return new List<bool> { true, false, true, true, false };
            yield return new List<char> { 'a', '\0', 'é', '￿' };
            yield return new List<float> { float.MinValue, -0f, 0.1f, float.NaN, float.PositiveInfinity };
            yield return new List<double> { double.MinValue, -0d, 0.1d, double.NaN, double.NegativeInfinity };
            yield return new List<decimal> { 0m, -1.5m, 100.00m, 1.2345678901234567890123456789m, decimal.MinValue, decimal.MaxValue };
            yield return new List<Guid> { Guid.Empty, new Guid("0f8fad5b-d9cb-469f-a165-70867728950e"), Guid.NewGuid() };
            yield return new List<TimeSpan> { TimeSpan.Zero, TimeSpan.MinValue, TimeSpan.MaxValue, TimeSpan.FromMilliseconds(1234.5) };
            yield return new List<DateTime>
            {
                DateTime.MinValue,
                DateTime.MaxValue,
                new DateTime(2026, 10, 12, 9, 30, 0, DateTimeKind.Utc),
                new DateTime(2026, 10, 12, 9, 30, 0, DateTimeKind.Local),
            };

            // One element, and none: the list ends on its first or its only token.
            yield return new List<int> { 7 };
            yield return new List<int>();
            yield return new List<decimal>();
            yield return new List<bool>();
        }

        private static IEnumerable<TestCaseData> ListCases()
        {
            foreach (IList list in Lists())
                yield return new TestCaseData(list).SetArgDisplayNames(list.GetType().GetGenericArguments()[0].Name + " x" + list.Count);
        }

        [TestCaseSource(nameof(ListCases))]
        public void ToObject_RootList_ReadsTheSameAsTheBoxingPath(IList list)
        {
            Guard.NotNull(list, nameof(list));
            byte[] bytes = Bjson.ToBjson(list);

            object? direct = Read(bytes, list.GetType(), primitive: true, out int directLists);
            object? boxing = Read(bytes, list.GetType(), primitive: false, out int boxingLists);

            // sbyte is the one element type here the direct path leaves to the boxing one.
            directLists.Should().Be(list is List<sbyte> ? 0 : 1, "the direct path must be what read a {0}", list.GetType());
            boxingLists.Should().Be(0);
            direct.Should().BeOfType(list.GetType());
            Cast((IList)direct).Should().Equal(Cast(list));
            Cast((IList)direct).Should().Equal(Cast((IList)boxing!));
        }

        [Test]
        public void ToObject_Member_ReadsTheSameAsTheBoxingPath()
        {
            ListsHolder original = ListsHolder.Create();
            byte[] bytes = Bjson.ToBjson(original);

            ListsHolder direct = (ListsHolder)Read(bytes, typeof(ListsHolder), primitive: true, out int directLists)!;
            ListsHolder boxing = (ListsHolder)Read(bytes, typeof(ListsHolder), primitive: false, out _)!;

            // Every list in the holder but the one of strings.
            directLists.Should().Be(16);
            direct.Should().BeEquivalentTo(original, o => o.WithStrictOrdering());
            direct.Should().BeEquivalentTo(boxing, o => o.WithStrictOrdering());
            Bjson.ToBjson(direct).Should().Equal(bytes);
        }

        [Test]
        public void ToObject_MemberOfAnotherWidth_GivesTheBoxingPathsOutcome()
        {
            byte[] longs = Bjson.ToBjson(new LongHolder { Items = { 1, 2, 3 } }, Untyped);

            AssertSameOutcome(Try(longs, typeof(IntHolder), primitive: true), Try(longs, typeof(IntHolder), primitive: false));
        }

        [Test]
        public void ToObject_MixedList_GivesTheBoxingPathsOutcome()
        {
            byte[] mixed = Bjson.ToBjson(new ObjectHolder { Items = { 1, "two", 3 } }, Untyped);

            AssertSameOutcome(Try(mixed, typeof(IntHolder), primitive: true), Try(mixed, typeof(IntHolder), primitive: false));
        }

        [Test]
        public void ToObject_ListWithANull_GivesTheBoxingPathsOutcome()
        {
            byte[] withNull = Bjson.ToBjson(new ObjectHolder { Items = { 1, null!, 3 } }, Untyped);

            AssertSameOutcome(Try(withNull, typeof(IntHolder), primitive: true), Try(withNull, typeof(IntHolder), primitive: false));
        }

        [Test]
        public void ToObject_ListWhoseLastElementIsAnotherWidth_GivesTheBoxingPathsOutcome()
        {
            // The first 5,000 elements are read straight into the list before the mismatch turns up.
            ObjectHolder late = new ObjectHolder();
            for (int i = 0; i < 5000; i++)
                late.Items.Add(i);
            late.Items.Add(5000L);
            byte[] bytes = Bjson.ToBjson(late, Untyped);

            AssertSameOutcome(Try(bytes, typeof(IntHolder), primitive: true), Try(bytes, typeof(IntHolder), primitive: false));
        }

        [Test]
        public void ToObject_ListWithATrailingComma_KeepsTheElementsReadBeforeIt()
        {
            // The one mismatch the boxing path reads successfully: the list ends where an element should start.
            // The direct path has the first elements by then and must hand them over, not drop or re-read them.
            byte[] clean = Bjson.ToBjson(new List<int> { 1, 2, 3 }, Untyped);
            int end = Array.LastIndexOf(clean, Tokens.ArrayEnd);
            byte[] trailing = new byte[clean.Length + 1];
            Array.Copy(clean, trailing, end);
            trailing[end] = Tokens.Comma;
            Array.Copy(clean, end, trailing, end + 1, clean.Length - end);

            List<int> direct = (List<int>)Read(trailing, typeof(List<int>), primitive: true, out int directLists)!;
            List<int> boxing = (List<int>)Read(trailing, typeof(List<int>), primitive: false, out _)!;

            directLists.Should().Be(0, "the list did not end where the direct path expects");
            boxing.Should().Equal(1, 2, 3);
            direct.Should().Equal(boxing);
        }

        [TestCase(11)]
        [TestCase(65)]
        [TestCase(1025)]
        [TestCase(100001)]
        public void ToObject_ListOfInt_DoesNotKeepMuchSpareCapacity(int count)
        {
            byte[] bytes = Bjson.ToBjson(new List<int>(System.Linq.Enumerable.Range(0, count)));

            List<int> direct = (List<int>)Read(bytes, typeof(List<int>), primitive: true, out _)!;

            direct.Should().HaveCount(count);
            // List growth alone would leave 11 ints in 16 slots and 100,001 in 131,072; the boxing path left count.
            direct.Capacity.Should().BeLessThanOrEqualTo(count + Math.Max(count / 8, 5), "a long-lived list must not pin up to twice its content");
        }

        private static void AssertSameOutcome(Outcome direct, Outcome boxing)
        {
            (direct.Error is null).Should().Be(boxing.Error is null, "one path read what the other rejected");
            direct.Error?.GetType().Should().Be(boxing.Error?.GetType());
            if (direct.Error is null)
                Bjson.ToBjson(direct.Value).Should().Equal(Bjson.ToBjson(boxing.Value));
        }

        [Test]
        public void ToObject_SeededDamagedBytes_BothPathsThrowOrAgree()
        {
            byte[] bytes = Bjson.ToBjson(ListsHolder.Create());
            Random random = new Random(20261012);

            for (int round = 0; round < 600; round++)
            {
                byte[] damaged = (byte[])bytes.Clone();
                if (round % 3 == 0)
                    damaged = damaged.AsSpan(0, random.Next(1, damaged.Length)).ToArray();
                else
                    damaged[random.Next(damaged.Length)] = (byte)random.Next(256);

                Outcome direct = Try(damaged, typeof(ListsHolder), primitive: true);
                Outcome boxing = Try(damaged, typeof(ListsHolder), primitive: false);

                (direct.Error is null).Should().Be(boxing.Error is null, "round {0}: one path read what the other rejected", round);
                direct.Error?.GetType().Should().Be(boxing.Error?.GetType(), "round {0}: the paths failed differently", round);
                if (direct.Error is null)
                    Bjson.ToBjson(direct.Value).Should().Equal(Bjson.ToBjson(boxing.Value), "round {0}", round);
            }
        }

        [Test]
        public void ToObject_ListOfDecimal_DoesNotBoxEachElement()
        {
            byte[] bytes = Bjson.ToBjson(DecimalHolder.Create(1000));

            long boxing = Allocated(bytes, typeof(DecimalHolder), primitive: false);
            long direct = Allocated(bytes, typeof(DecimalHolder), primitive: true);

            // A boxed decimal is 32 bytes, and the staging list holds a reference to each besides.
            (boxing - direct)
                .Should()
                .BeGreaterThan(24 * 1000, "the direct path allocated {0} bytes less than the boxing one", boxing - direct);
        }

        [Test]
        public void ToObject_ListOfInt_DoesNotBoxEachElement()
        {
            byte[] bytes = Bjson.ToBjson(new List<int>(System.Linq.Enumerable.Range(0, 1000)));

            long boxing = Allocated(bytes, typeof(List<int>), primitive: false);
            long direct = Allocated(bytes, typeof(List<int>), primitive: true);

            (boxing - direct).Should().BeGreaterThan(16 * 1000, "the direct path allocated {0} bytes less than the boxing one", boxing - direct);
        }

        private static IEnumerable<object> Cast(IList list)
        {
            foreach (object item in list)
                yield return item;
        }

        private static object? Read(byte[] bytes, Type type, bool primitive) => Read(bytes, type, primitive, out _);

        private static object? Read(byte[] bytes, Type type, bool primitive, out int primitiveLists)
        {
            Deserializer deserializer = new Deserializer(new BjsonParameters()) { PrimitiveListReads = primitive };
            object? read = deserializer.ToObject(bytes, type);
            primitiveLists = deserializer.PrimitiveListsRead;
            return read;
        }

        private static Outcome Try(byte[] bytes, Type type, bool primitive)
        {
            try
            {
                return new Outcome(Read(bytes, type, primitive), null);
            }
#pragma warning disable CA1031 // Any failure is the outcome being compared: the two paths must fail alike.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                return new Outcome(null, ex);
            }
        }

        private static long Allocated(byte[] bytes, Type type, bool primitive) => AllocationProbe.PerCall(() => Read(bytes, type, primitive));

        private sealed record Outcome(object? Value, Exception? Error);

        public sealed class LongHolder
        {
            public List<long> Items { get; set; } = new List<long>();
        }

        public sealed class IntHolder
        {
            public List<int> Items { get; set; } = new List<int>();
        }

        public sealed class ObjectHolder
        {
            public List<object> Items { get; set; } = new List<object>();
        }

        public sealed class DecimalHolder
        {
            public List<decimal> Items { get; set; } = new List<decimal>();

            internal static DecimalHolder Create(int count)
            {
                DecimalHolder holder = new DecimalHolder();
                for (int i = 0; i < count; i++)
                    holder.Items.Add(i + 0.5m);
                return holder;
            }
        }

        public sealed class ListsHolder
        {
            public List<int> Ints { get; set; } = new List<int>();

            public List<uint> UInts { get; set; } = new List<uint>();

            public List<long> Longs { get; set; } = new List<long>();

            public List<ulong> ULongs { get; set; } = new List<ulong>();

            public List<short> Shorts { get; set; } = new List<short>();

            public List<ushort> UShorts { get; set; } = new List<ushort>();

            public List<byte> Bytes { get; set; } = new List<byte>();

            public List<bool> Bools { get; set; } = new List<bool>();

            public List<char> Chars { get; set; } = new List<char>();

            public List<float> Floats { get; set; } = new List<float>();

            public List<double> Doubles { get; set; } = new List<double>();

            public List<decimal> Decimals { get; set; } = new List<decimal>();

            public List<Guid> Guids { get; set; } = new List<Guid>();

            public List<TimeSpan> Spans { get; set; } = new List<TimeSpan>();

            public List<DateTime> Times { get; set; } = new List<DateTime>();

            public List<int> Empty { get; set; } = new List<int>();

            public List<string?> Strings { get; set; } = new List<string?>();

            internal static ListsHolder Create()
            {
                return new ListsHolder
                {
                    Ints = { int.MinValue, 0, int.MaxValue },
                    UInts = { 0, uint.MaxValue },
                    Longs = { long.MinValue, long.MaxValue },
                    ULongs = { 0, ulong.MaxValue },
                    Shorts = { short.MinValue, short.MaxValue },
                    UShorts = { 0, ushort.MaxValue },
                    Bytes = { 0, 255 },
                    Bools = { true, false, true },
                    Chars = { 'a', 'é' },
                    Floats = { 0.1f, -0f, float.MaxValue },
                    Doubles = { 0.1d, double.MinValue },
                    Decimals = { 0m, -1.5m, decimal.MaxValue },
                    Guids = { Guid.Empty, new Guid("0f8fad5b-d9cb-469f-a165-70867728950e") },
                    Spans = { TimeSpan.Zero, TimeSpan.FromDays(3) },
                    Times = { new DateTime(2026, 10, 12, 9, 30, 0, DateTimeKind.Utc) },
                    Strings = { "a", null, "c" },
                };
            }
        }
    }
}
