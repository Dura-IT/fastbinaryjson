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
            byte[] bytes = Bjson.ToBjson(testCase.Build(), testCase.Parameters());

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

        /// <summary>
        /// Damaged input, from real writer output with seeded single-byte replacements and
        /// truncations: both paths throw, or both produce the same graph.
        /// </summary>
        /// <remarks>
        /// The one accepted difference is a key repeated within an object, which damage can produce by
        /// turning one name into another: the parser throws on it and the reader lets the last win.
        /// Typed arrays are written off, because a damaged element count makes both paths try to
        /// allocate an array of that size - slow and memory hungry, and not a reader question.
        /// </remarks>
        [TestCaseSource(nameof(MutationSources))]
        public void ToObject_DamagedInput_BothThrowOrBothMatch(EquivalenceCase source)
        {
            byte[] original = Bjson.ToBjson(source.Build(), source.Parameters());
            Random random = new Random(20260930 + original.Length);
            int succeeded = 0;

            for (int i = 0; i < 400; i++)
            {
                byte[] damaged =
                    i % 5 == 4
                        ? original.Take(random.Next(1, original.Length)).ToArray()
                        : Replace(original, random.Next(original.Length), (byte)random.Next(256));

                Outcome twoStep = Read(damaged, source, oneStep: false);
                Outcome oneStep = Read(damaged, source, oneStep: true);
                string context = "mutation " + i;

                if (twoStep.Error != null)
                {
                    if (oneStep.Error == null && IsDuplicateKey(twoStep.Error))
                        continue;

                    oneStep.Error.Should().NotBeNull("{0}: the two-step path threw {1}", context, twoStep.Error.GetType().Name);
                    continue;
                }

                oneStep.Error.Should().BeNull("{0}: the two-step path succeeded", context);
                oneStep
                    .Value.Should()
                    .BeEquivalentTo(twoStep.Value, o => o.PreferringRuntimeMemberTypes().WithStrictOrdering().IgnoringCyclicReferences(), context);
                AssertSameShape(twoStep.Value, oneStep.Value, context, new HashSet<object>(ReferenceEqualityComparer.Instance));
                succeeded++;
            }

            succeeded
                .Should()
                .BeGreaterThan(0, "some damage (a changed digit, a changed character) still decodes, and those are the cases that compare graphs");
        }

        public static IEnumerable<EquivalenceCase> MutationSources()
        {
            Func<BjsonParameters> untyped = () => new BjsonParameters { UseTypedArrays = false };
            yield return new EquivalenceCase("primitives", GoldenCorpus.BuildPrimitives, untyped, typeof(Primitives));
            yield return new EquivalenceCase("invoice", GoldenCorpus.BuildInvoice, untyped, typeof(Invoice));
            yield return new EquivalenceCase("shapes", GoldenCorpus.BuildShapes, untyped, typeof(ShapeBox));
            yield return new EquivalenceCase("shared-reference", GoldenCorpus.BuildSharedReference, untyped, typeof(ReferenceBox));
            yield return new EquivalenceCase("order", PayloadFactory.CreateNestedOrder, untyped, typeof(Order));
            yield return new EquivalenceCase("kitchen-sink", EqKitchenSink.Build, untyped, typeof(EqKitchenSink));
            yield return new EquivalenceCase("typed-members", EqTypedMembers.Build, untyped, typeof(EqTypedMembers));
            yield return new EquivalenceCase("root-node-list", BuildNodeList, untyped, typeof(List<EqNode?>));
            yield return new EquivalenceCase("root-polymorphic-list", BuildPolymorphicList, untyped, typeof(List<EqBase>));
        }

        private static byte[] Replace(byte[] original, int index, byte value)
        {
            byte[] copy = (byte[])original.Clone();
            copy[index] = value;
            return copy;
        }

        private static bool IsDuplicateKey(Exception error)
        {
            return error is ArgumentException && error.Message.Contains("same key", StringComparison.Ordinal);
        }

        [Test]
        public void ToObject_SharedReference_IsOneInstanceOnBothPaths()
        {
            byte[] bytes = Bjson.ToBjson(GoldenCorpus.BuildSharedReference());

            ReferenceBox restored = (ReferenceBox)ReadWith(bytes, typeof(ReferenceBox), new BjsonParameters(), oneStep: true)!;

            restored.Third.Should().BeSameAs(restored.First);
            restored.Second.Should().NotBeSameAs(restored.First);
        }

        [Test]
        public void ToObject_Cycle_IsRestoredAsACycle()
        {
            EqNode a = new EqNode { Name = "a" };
            a.Next = new EqNode { Name = "b", Next = a };

            EqNode restored = (EqNode)ReadWith(Bjson.ToBjson(a), typeof(EqNode), new BjsonParameters(), oneStep: true)!;

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
            byte[] bytes = Bjson.ToBjson(value);

            Outcome oneStep = Read(bytes, new EquivalenceCase("bag", () => value, () => new BjsonParameters(), typeof(EqKitchenSink)), oneStep: true);

            oneStep.Error.Should().BeNull();
            oneStep.Fallbacks.Should().BeGreaterThan(0);
            ((EqKitchenSink)oneStep.Value!).Bag.Should().BeAssignableTo<Dictionary<string, object>>();
        }

        /// <summary>
        /// Equivalence would also hold if every member still went through the boxing setter, so this
        /// pins that each primitive member was set by the typed path.
        /// </summary>
        [Test]
        public void ToObject_PrimitiveMembers_AreSetWithoutBoxing()
        {
            byte[] bytes = Bjson.ToBjson(EqTypedMembers.Build());
            Deserializer deserializer = new Deserializer(new BjsonParameters());

            deserializer.ToObject<EqTypedMembers>(bytes);

            deserializer.TypedSets.Should().Be(EqTypedMembers.TypedMemberCount);
        }

        /// <summary>
        /// The private setter and the get-only property's backing field take the typed path too.
        /// </summary>
        [Test]
        public void ToObject_ReadOnlyMembers_AreSetWithoutBoxing()
        {
            BjsonParameters parameters = new BjsonParameters { ShowReadOnlyProperties = true };
            Deserializer deserializer = new Deserializer(parameters);

            EqReadOnlyMembers restored = deserializer.ToObject<EqReadOnlyMembers>(Bjson.ToBjson(EqReadOnlyMembers.Build(), parameters))!;

            restored.PrivateSet.Should().Be(42);
            restored.GetOnly.Should().Be(EqReadOnlyMembers.Build().GetOnly);
            deserializer.TypedSets.Should().Be(2);
        }

        /// <summary>
        /// A struct's boxing setter copies the struct out of its box and back per member, so the typed
        /// path is not offered there - its members must still arrive.
        /// </summary>
        [Test]
        public void ToObject_StructMembers_TakeTheBoxingSetter()
        {
            EqPointHolder value = new EqPointHolder
            {
                Point = new EqPoint { X = 3, Y = -4 },
            };
            Deserializer deserializer = new Deserializer(new BjsonParameters());

            EqPointHolder restored = deserializer.ToObject<EqPointHolder>(Bjson.ToBjson(value))!;

            restored.Point.Should().Be(new EqPoint { X = 3, Y = -4 });
            deserializer.TypedSets.Should().Be(0);
        }

        public static IEnumerable<EquivalenceCase> Cases()
        {
            foreach (GoldenCase golden in GoldenFileTests.Cases())
            {
                object built = golden.Build();
                yield return new EquivalenceCase(
                    "golden/" + golden.Name,
                    golden.Build,
                    golden.Parameters,
                    built.GetType(),
                    MustSucceed: golden.BytesOnlyReason == null,
                    StandardShape: IsStandardGolden(golden.Name)
                );
            }

            foreach ((string name, Func<BjsonParameters> parameters, bool extensions) in Variants())
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
                yield return Case("typed-members", EqTypedMembers.Build, name, parameters, true);
            }

            Func<BjsonParameters> showReadOnly = () => new BjsonParameters { ShowReadOnlyProperties = true };
            yield return Case("read-only-members", EqReadOnlyMembers.Build, "show-read-only", showReadOnly, true);

            // No $type, so the declared type decides the members - and their tokens do not match.
            Func<BjsonParameters> noExtensions = () => new BjsonParameters { UseExtensions = false };
            yield return new EquivalenceCase("token-wider-than-member/no-extensions", EqWidths.Build, noExtensions, typeof(EqWidened), MustSucceed: false);
            yield return new EquivalenceCase(
                "token-into-nullable-member/no-extensions",
                EqWidths.Build,
                noExtensions,
                typeof(EqNullableWidths),
                StandardShape: true
            );
        }

        private static IEnumerable<(string Name, Func<BjsonParameters> Parameters, bool Extensions)> Variants()
        {
            yield return ("defaults", () => new BjsonParameters(), true);
            yield return ("utf8", () => new BjsonParameters { UseUnicodeStrings = false }, true);
            yield return ("no-global-types", () => new BjsonParameters { UsingGlobalTypes = false }, true);
            yield return ("nulls", () => new BjsonParameters { SerializeNulls = true }, true);
            yield return ("untyped-arrays", () => new BjsonParameters { UseTypedArrays = false }, true);
            yield return ("utc", () => new BjsonParameters { UseUtcDateTime = true }, true);
            yield return ("no-extensions", () => new BjsonParameters { UseExtensions = false }, false);
        }

        private static EquivalenceCase Case<T>(
            string shape,
            Func<T> build,
            string variant,
            Func<BjsonParameters> parameters,
            bool standard,
            bool mustSucceed = true
        )
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
                new EqNode
                {
                    Name = "one",
                    Value = 1,
                    Children = new List<EqNode> { new EqNode { Name = "leaf" } },
                },
                null,
                shared,
                new EqNode
                {
                    Name = "two",
                    Value = 2,
                    Next = shared,
                },
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
            if (!expected.GetType().IsValueType && !seen.Add(expected))
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
#pragma warning disable CA1031 // The outcome of a read, whatever it throws, is what the two readers are compared on.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                return new Outcome(null, ex, deserializer.OneStepFallbacks);
            }
        }

        private static object? ReadWith(byte[] bytes, Type type, BjsonParameters parameters, bool oneStep)
        {
            return new Deserializer(parameters) { OneStep = oneStep }.ToObject(bytes, type);
        }

        private sealed record Outcome(object? Value, Exception? Error, int Fallbacks);
    }

    /// <summary>
    /// Public only because NUnit requires a public parameter type on a [TestCaseSource] method.
    /// </summary>
    public sealed record EquivalenceCase(
        string Name,
        Func<object> Build,
        Func<BjsonParameters> Parameters,
        Type Type,
        bool MustSucceed = true,
        bool StandardShape = false
    )
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

    public sealed class EqPointHolder
    {
        public EqPoint Point { get; set; }
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
            return new EqNestedLists
            {
                Nested = new List<List<int>>
                {
                    new List<int> { 1 },
                    new List<int>(),
                    new List<int> { 2, 3 },
                },
            };
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
                Child = new EqNode
                {
                    Name = "child",
                    Value = 1,
                    Next = shared,
                },
                Empty = new EqNode(),
                Nodes = new List<EqNode>
                {
                    shared,
                    new EqNode
                    {
                        Name = "n2",
                        Children = new List<EqNode> { new EqNode { Name = "n3" } },
                    },
                },
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
                Mixed = new List<EqBase>
                {
                    new EqLeft { Left = 1 },
                    new EqRight { Right = 2 },
                },
            };
        }
    }

    /// <summary>
    /// Every primitive the reader sets without boxing - as a property, as a nullable property with
    /// and without a value, and as a field.
    /// </summary>
    public sealed class EqTypedMembers
    {
        /// <summary>
        /// Members written with a primitive token at default parameters - each one a typed set.
        /// </summary>
        internal const int TypedMemberCount = 22;

        public int CountField;

        public Guid? IdField;

        public int Int32 { get; set; }

        public long Int64 { get; set; }

        public short Int16 { get; set; }

        public ushort UInt16 { get; set; }

        public uint UInt32 { get; set; }

        public ulong UInt64 { get; set; }

        public byte Byte { get; set; }

        public bool Flag { get; set; }

        public bool Off { get; set; }

        public double Double { get; set; }

        public float Single { get; set; }

        public decimal Decimal { get; set; }

        public DateTime When { get; set; }

        public DateTimeOffset At { get; set; }

        public TimeSpan Span { get; set; }

        public Guid Id { get; set; }

        public char Letter { get; set; }

        public int? MaybeInt { get; set; }

        public DateTime? MaybeWhen { get; set; }

        public bool? MaybeFlag { get; set; }

        public long? NoLong { get; set; }

        internal static EqTypedMembers Build()
        {
            return new EqTypedMembers
            {
                CountField = 3,
                IdField = new Guid("6f9619ff-8b86-d011-b42d-00c04fc964ff"),
                Int32 = int.MinValue,
                Int64 = long.MaxValue,
                Int16 = -12345,
                UInt16 = ushort.MaxValue,
                UInt32 = uint.MaxValue,
                UInt64 = ulong.MaxValue,
                Byte = 200,
                Flag = true,
                Off = false,
                Double = -1.5e300,
                Single = 3.25f,
                Decimal = -79228162514264337593543950335m,
                When = new DateTime(2026, 10, 1, 9, 30, 0, DateTimeKind.Utc),
                At = new DateTimeOffset(2026, 10, 1, 9, 30, 0, TimeSpan.FromHours(-5)),
                Span = TimeSpan.FromTicks(-123456789),
                Id = new Guid("0f8fad5b-d9cb-469f-a165-70867728950e"),
                Letter = 'é',
                MaybeInt = 7,
                MaybeWhen = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Local),
                MaybeFlag = false,
                NoLong = null,
            };
        }
    }

    /// <summary>
    /// Members only writable with ShowReadOnlyProperties: a private setter, and a get-only auto
    /// property set through its backing field.
    /// </summary>
    /// <remarks>
    /// Read only with that parameter on. Getproperties caches by type name alone, so whichever
    /// parameters a type is first read with stick - this type is never read any other way.
    /// </remarks>
    public sealed class EqReadOnlyMembers
    {
        public EqReadOnlyMembers() { }

        internal EqReadOnlyMembers(int privateSet, DateTime getOnly)
        {
            PrivateSet = privateSet;
            GetOnly = getOnly;
        }

        public int PrivateSet { get; private set; }

        public DateTime GetOnly { get; }

        internal static EqReadOnlyMembers Build()
        {
            return new EqReadOnlyMembers(42, new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc));
        }
    }

    /// <summary>
    /// Written, then read as <see cref="EqWidened"/> or <see cref="EqNullableWidths"/> without $type,
    /// so the token on the wire is not the one the reading member's type is written with.
    /// </summary>
    public sealed class EqWidths
    {
        public int Narrow { get; set; }

        public float Single { get; set; }

        internal static EqWidths Build()
        {
            return new EqWidths { Narrow = 5, Single = 1.5f };
        }
    }

    public sealed class EqWidened
    {
        public long Narrow { get; set; }

        public double Single { get; set; }
    }

    public sealed class EqNullableWidths
    {
        public int? Narrow { get; set; }

        public float? Single { get; set; }
    }
}
