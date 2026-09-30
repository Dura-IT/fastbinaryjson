using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;

using AwesomeAssertions;

using DuraIT.FastBinaryJson;

using FastBinaryJson.Benchmarks.Corpus;
using FastBinaryJson.UnitTests.Golden;

using NUnit.Framework;

namespace FastBinaryJson.UnitTests.RoundTrip
{
    /*
     * The one-step TypedReader against the two-step path it replaces, on the same bytes.
     *
     * The two-step path is the reference: whatever it produces, the reader must produce an
     * equivalent graph of the same runtime types in the same order; whatever it throws on, the reader
     * must throw on too. Every case runs on both targets - this file is part of the shared test source.
     *
     * Equivalence alone would pass if the reader fell back to the two-step path for everything, so
     * standard writer output is also required to be read with no fallback at all.
     */
    [TestFixture]
    [TestOf(typeof(TypedReader))]
    public sealed class ReaderEquivalenceTests
    {
        [TestCaseSource(nameof(Cases))]
        public void ToObject_OneStep_MatchesTwoStep(EquivalenceCase testCase)
        {
            byte[] bytes = BJSON.ToBJSON(testCase.Build(), testCase.Parameters());

            Outcome twoStep = Read(bytes, testCase, oneStep: false);
            Outcome oneStep = Read(bytes, testCase, oneStep: true);

            if (twoStep.Error != null)
            {
                testCase.MustSucceed.Should().BeFalse("the two-step path threw {0}", twoStep.Error);
                oneStep.Error.Should().NotBeNull("the two-step path threw {0}, so the reader must fail too", twoStep.Error.GetType().Name);
                return;
            }

            oneStep.Error.Should().BeNull();
            oneStep.Value.Should().BeEquivalentTo(twoStep.Value, o => o.PreferringRuntimeMemberTypes().WithStrictOrdering().IgnoringCyclicReferences());
            AssertSameShape(twoStep.Value, oneStep.Value, "root", new HashSet<object>(ReferenceEqualityComparer.Instance));
            if (testCase.StandardShape)
                oneStep.Fallbacks.Should().Be(0, "writer output of a standard shape must not need the two-step path");
        }

        [Test]
        public void ToObject_SharedReference_IsOneInstanceOnBothPaths()
        {
            byte[] bytes = BJSON.ToBJSON(GoldenCorpus.BuildSharedReference());

            ReferenceBox restored = (ReferenceBox)ReadWith(bytes, typeof(ReferenceBox), new BJSONParameters(), oneStep: true)!;

            restored.Third.Should().BeSameAs(restored.First);
            restored.Second.Should().NotBeSameAs(restored.First);
        }

        [Test]
        public void ToObject_Cycle_IsRestoredAsACycle()
        {
            EqNode a = new EqNode { Name = "a" };
            a.Next = new EqNode { Name = "b", Next = a };

            EqNode restored = (EqNode)ReadWith(BJSON.ToBJSON(a), typeof(EqNode), new BJSONParameters(), oneStep: true)!;

            restored.Next!.Name.Should().Be("b");
            restored.Next.Next.Should().BeSameAs(restored);
        }

        /// <summary>
        /// A member typed object whose value was a string-keyed dictionary has no $type, so
        /// ParseDictionary hands the dictionary itself back. The reader has to give that object up;
        /// this pins that it does, and that the result still matches.
        /// </summary>
        [Test]
        public void ToObject_ObjectMemberWithoutType_FallsBackAndStillMatches()
        {
            EqKitchenSink value = EqKitchenSink.Build();
            byte[] bytes = BJSON.ToBJSON(value);

            Outcome oneStep = Read(bytes, new EquivalenceCase("bag", () => value, () => new BJSONParameters(), typeof(EqKitchenSink)), oneStep: true);

            oneStep.Error.Should().BeNull();
            oneStep.Fallbacks.Should().BeGreaterThan(0);
            ((EqKitchenSink)oneStep.Value!).Bag.Should().BeAssignableTo<Dictionary<string, object>>();
        }

        public static IEnumerable<EquivalenceCase> Cases()
        {
            foreach (GoldenCase golden in GoldenFileTests.Cases())
            {
                object built = golden.Build();
                yield return new EquivalenceCase("golden/" + golden.Name, golden.Build, golden.Parameters, built.GetType(), MustSucceed: golden.BytesOnlyReason == null, StandardShape: IsStandardGolden(golden.Name));
            }

            foreach ((string name, Func<BJSONParameters> parameters, bool extensions) in Variants())
            {
                yield return Case("flat", PayloadFactory.CreateFlatPrimitives, name, parameters, true);
                yield return Case("order", PayloadFactory.CreateNestedOrder, name, parameters, true);
                yield return Case("large-collection", PayloadFactory.CreateLargeCollection, name, parameters, true);
                yield return Case("guid-dense", PayloadFactory.CreateCorrelationRecords, name, parameters, true);
                yield return Case("char-holder", PayloadFactory.CreateCharHolder, name, parameters, true);
                yield return Case("long-key-dictionary", PayloadFactory.CreateLongKeyDictionary, name, parameters, false);
                yield return Case("shape-catalogue", PayloadFactory.CreateShapeCatalogue, name, parameters, extensions, mustSucceed: extensions);
                yield return Case("kitchen-sink", EqKitchenSink.Build, name, parameters, false, mustSucceed: extensions);
                yield return Case("root-node-list", BuildNodeList, name, parameters, true);
                yield return Case("root-polymorphic-list", BuildPolymorphicList, name, parameters, extensions, mustSucceed: extensions);
                yield return Case("empty-object", () => new EqNode(), name, parameters, true);
                // Upstream never read these: CreateGenericList adds the inner List<object> as is.
                yield return Case("nested-value-lists", EqNestedLists.Build, name, parameters, false, mustSucceed: false);
                yield return Case("empty-list", () => new List<EqNode>(), name, parameters, true);
            }
        }

        private static IEnumerable<(string Name, Func<BJSONParameters> Parameters, bool Extensions)> Variants()
        {
            yield return ("defaults", () => new BJSONParameters(), true);
            yield return ("utf8", () => new BJSONParameters { UseUnicodeStrings = false }, true);
            yield return ("no-global-types", () => new BJSONParameters { UsingGlobalTypes = false }, true);
            yield return ("nulls", () => new BJSONParameters { SerializeNulls = true }, true);
            yield return ("untyped-arrays", () => new BJSONParameters { UseTypedArrays = false }, true);
            yield return ("no-extensions", () => new BJSONParameters { UseExtensions = false }, false);
        }

        private static EquivalenceCase Case<T>(string shape, Func<T> build, string variant, Func<BJSONParameters> parameters, bool standard, bool mustSucceed = true)
            where T : notnull
        {
            return new EquivalenceCase(shape + "/" + variant, () => build(), parameters, typeof(T), mustSucceed, standard);
        }

        // Golden cases whose shape the reader must read end to end, without a single fallback.
        private static bool IsStandardGolden(string name)
        {
            return name.StartsWith("primitives", StringComparison.Ordinal)
                || name.StartsWith("invoice", StringComparison.Ordinal)
                || name == "shapes-polymorphic"
                || name == "shared-reference"
                || name == "utc-datetime";
        }

        private static List<EqNode?> BuildNodeList()
        {
            EqNode shared = new EqNode { Name = "shared", Value = 3 };
            return new List<EqNode?>
            {
                new EqNode { Name = "one", Value = 1, Children = new List<EqNode> { new EqNode { Name = "leaf" } } },
                null,
                shared,
                new EqNode { Name = "two", Value = 2, Next = shared },
            };
        }

        private static List<EqBase> BuildPolymorphicList()
        {
            return new List<EqBase>
            {
                new EqLeft { Label = "l1", Left = 1 },
                new EqRight { Label = "r1", Right = 1.5 },
                new EqLeft { Label = "l2", Left = 2 },
            };
        }

        /// <summary>
        /// Same runtime type at every node, and the same DateTimeKind - neither of which
        /// BeEquivalentTo checks: it compares a List&lt;object&gt; and an object[] with equal items as
        /// equal, and DateTime equality ignores Kind.
        /// </summary>
        private static void AssertSameShape(object? expected, object? actual, string path, HashSet<object> seen)
        {
            if (expected is null || actual is null)
            {
                (actual is null).Should().Be(expected is null, "at {0}", path);
                return;
            }

            actual.GetType().Should().Be(expected.GetType(), "at {0}", path);
            if (expected is DateTime expectedTime)
            {
                ((DateTime)actual).Kind.Should().Be(expectedTime.Kind, "at {0}", path);
                return;
            }

            if (expected is string || expected.GetType().IsPrimitive || expected.GetType().IsEnum)
                return;
            if (expected.GetType().IsValueType == false && seen.Add(expected) == false)
                return;

            if (expected is IDictionary expectedMap)
            {
                IDictionary actualMap = (IDictionary)actual;
                foreach (object key in expectedMap.Keys)
                    AssertSameShape(expectedMap[key], actualMap[key], path + "[" + key + "]", seen);
                return;
            }

            if (expected is IEnumerable expectedItems)
            {
                object?[] left = expectedItems.Cast<object?>().ToArray();
                object?[] right = ((IEnumerable)actual).Cast<object?>().ToArray();
                right.Length.Should().Be(left.Length, "at {0}", path);
                for (int i = 0; i < left.Length; i++)
                    AssertSameShape(left[i], right[i], path + "[" + i + "]", seen);
                return;
            }

            // Framework values (Guid, TimeSpan, DateTimeOffset...) are leaves; their properties return
            // fresh instances of their own type, so walking them never ends.
            if (expected.GetType().Assembly == typeof(object).Assembly)
                return;

            const System.Reflection.BindingFlags Members = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance;
            foreach (System.Reflection.PropertyInfo property in expected.GetType().GetProperties(Members))
            {
                if (property.GetIndexParameters().Length == 0 && property.CanRead)
                    AssertSameShape(property.GetValue(expected), property.GetValue(actual), path + "." + property.Name, seen);
            }

            foreach (System.Reflection.FieldInfo field in expected.GetType().GetFields(Members))
                AssertSameShape(field.GetValue(expected), field.GetValue(actual), path + "." + field.Name, seen);
        }

        private static Outcome Read(byte[] bytes, EquivalenceCase testCase, bool oneStep)
        {
            Deserializer deserializer = new Deserializer(testCase.Parameters()) { OneStep = oneStep };
            try
            {
                return new Outcome(deserializer.ToObject(bytes, testCase.Type), null, deserializer.OneStepFallbacks);
            }
            catch (Exception ex)
            {
                return new Outcome(null, ex, deserializer.OneStepFallbacks);
            }
        }

        private static object? ReadWith(byte[] bytes, Type type, BJSONParameters parameters, bool oneStep)
        {
            return new Deserializer(parameters) { OneStep = oneStep }.ToObject(bytes, type);
        }

        private sealed record Outcome(object? Value, Exception? Error, int Fallbacks);
    }

    /// <summary>
    /// Public only because NUnit requires a public parameter type on a [TestCaseSource] method.
    /// </summary>
    public sealed record EquivalenceCase(string Name, Func<object> Build, Func<BJSONParameters> Parameters, Type Type, bool MustSucceed = true, bool StandardShape = false)
    {
        public override string ToString()
        {
            return Name;
        }
    }

    public sealed class EqNode
    {
        public string? Name { get; set; }

        public int Value { get; set; }

        public EqNode? Next { get; set; }

        public List<EqNode>? Children { get; set; }
    }

    public struct EqPoint
    {
        public int X;

        public int Y;
    }

    public enum EqKind
    {
        None,
        Some,
        Many,
    }

    public abstract class EqBase
    {
        public string? Label { get; set; }
    }

    public sealed class EqLeft : EqBase
    {
        public int Left { get; set; }
    }

    public sealed class EqRight : EqBase
    {
        public double Right { get; set; }
    }

    public sealed class EqNestedLists
    {
        public List<List<int>> Nested { get; set; } = new List<List<int>>();

        internal static EqNestedLists Build()
        {
            return new EqNestedLists { Nested = new List<List<int>> { new List<int> { 1 }, new List<int>(), new List<int> { 2, 3 } } };
        }
    }

    /// <summary>
    /// Every member kind ConvertValue distinguishes, so the reader's direct paths and its hand-offs
    /// are all exercised in one graph.
    /// </summary>
    public sealed class EqKitchenSink
    {
        public string? Text { get; set; }

        public int Number { get; set; }

        public long? MaybeLong { get; set; }

        public Guid Id { get; set; }

        public DateTime When { get; set; }

        public DateTimeOffset At { get; set; }

        public TimeSpan Span { get; set; }

        public decimal Amount { get; set; }

        public sbyte Signed { get; set; }

        public char Letter { get; set; }

        public byte[]? Blob { get; set; }

        public EqKind Kind { get; set; }

        public EqPoint Point { get; set; }

        public EqNode? Child { get; set; }

        public EqNode? Empty { get; set; }

        public List<EqNode> Nodes { get; set; } = new List<EqNode>();

        public List<int> Numbers { get; set; } = new List<int>();

        public List<string?> Strings { get; set; } = new List<string?>();

        public EqNode[]? NodeArray { get; set; }

        public int[]? IntArray { get; set; }

        public Dictionary<string, EqNode> ByName { get; set; } = new Dictionary<string, EqNode>();

        public Dictionary<int, string> ById { get; set; } = new Dictionary<int, string>();

        public NameValueCollection? Headers { get; set; }

        public object? Anything { get; set; }

        public object? Bag { get; set; }

        public EqBase? Polymorphic { get; set; }

        public List<EqBase> Mixed { get; set; } = new List<EqBase>();

        internal static EqKitchenSink Build()
        {
            EqNode shared = new EqNode { Name = "shared", Value = 7 };
            return new EqKitchenSink
            {
                Text = "text é 😀",
                Number = -5,
                MaybeLong = 1234567890123L,
                Id = new Guid("0f8fad5b-d9cb-469f-a165-70867728950e"),
                When = new DateTime(2026, 9, 30, 12, 0, 0, DateTimeKind.Utc),
                At = new DateTimeOffset(2026, 9, 30, 12, 0, 0, TimeSpan.FromHours(2)),
                Span = TimeSpan.FromMinutes(90),
                Amount = 12.345m,
                Signed = -42,
                Letter = 'Z',
                Blob = new byte[] { 1, 2, 3 },
                Kind = EqKind.Many,
                Point = new EqPoint { X = 3, Y = -4 },
                Child = new EqNode { Name = "child", Value = 1, Next = shared },
                Empty = new EqNode(),
                Nodes = new List<EqNode> { shared, new EqNode { Name = "n2", Children = new List<EqNode> { new EqNode { Name = "n3" } } } },
                Numbers = new List<int> { 1, 2, 3 },
                Strings = new List<string?> { "a", null, "c" },
                NodeArray = new[] { new EqNode { Name = "arr" } },
                IntArray = new[] { 9, 8 },
                ByName = new Dictionary<string, EqNode> { ["x"] = new EqNode { Name = "x" } },
                ById = new Dictionary<int, string> { [1] = "one" },
                Headers = new NameValueCollection { ["h"] = "v" },
                Anything = new EqNode { Name = "anything" },
                Bag = new Dictionary<string, object> { ["k"] = "v" },
                Polymorphic = new EqRight { Label = "p", Right = 2.5 },
                Mixed = new List<EqBase> { new EqLeft { Left = 1 }, new EqRight { Right = 2 } },
            };
        }
    }
}
