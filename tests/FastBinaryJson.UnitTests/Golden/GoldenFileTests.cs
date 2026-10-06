using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using AwesomeAssertions;
using DuraIT.FastBinaryJson;
using FastBinaryJson.UnitTests.RoundTrip;
using NUnit.Framework;

namespace FastBinaryJson.UnitTests.Golden
{
    /*
     * Byte-for-byte compatibility tests against committed wire-format files.
     *
     * These are the safety net for every later change to the serializer. A refactor that keeps all
     * 70 ported behaviour tests green can still shift a byte, and any consumer with stored history
     * would only find out when their old data failed to load. These files make that failure loud
     * and immediate instead.
     *
     * THREE couplings are deliberate and load-bearing:
     *
     *   1. The fixtures embed each fixture type's AssemblyQualifiedName in $type, which carries
     *      this assembly's NAME and VERSION. Both are pinned in the csproj for exactly that reason.
     *      Renaming this project, or bumping its assembly version, invalidates every fixture.
     *
     *   2. The typed-array fixtures additionally embed the RUNTIME's identity: an int[] or string[]
     *      written with UseTypedArrays carries its element type's AQN, which resolves to
     *      System.Private.CoreLib, Version=<TFM runtime version>. A TargetFramework bump therefore
     *      rewrites int-array-typed, string-array-typed and string-array-typed-v14 with no change
     *      to the serializer at all. MSBuild cannot pin the runtime version, so
     *      Fixtures_RuntimeVersion_MatchesWhatTheyWereGeneratedWith asserts it directly and fails
     *      with a message that says so - rather than leaving three byte diffs whose failure text
     *      invites a regeneration that silently blesses the change.
     *
     *   3. Field order in the output follows reflection's member order for each fixture type.
     *      Reordering properties in GoldenCorpus.cs is a format change, not a cosmetic edit.
     *
     * Regenerating: set FBJ_REGEN_GOLDEN=1 and run the suite. That REWRITES the committed bytes,
     * so only do it when a format change is intended, and review the resulting diff as carefully
     * as any source change. A regeneration run always fails at the end so it can never be mistaken
     * for a passing build, and the deserialize half stands down entirely while it runs - it would
     * otherwise be comparing against files being rewritten underneath it, in an order NUnit does
     * not define.
     */
    [TestFixture]
    [TestOf(typeof(BJSON))]
    public sealed class GoldenFileTests
    {
        private const string RegenerateVariable = "FBJ_REGEN_GOLDEN";

        private const string CoreLibraryName = "System.Private.CoreLib";

        private const string LegacyEqualStructs = "legacy-equal-structs-as-reference";

        private static readonly Version ExpectedRuntimeVersion = new Version(10, 0, 0, 0);

        private static bool Regenerating
        {
            get { return Environment.GetEnvironmentVariable(RegenerateVariable) == "1"; }
        }

        internal static IEnumerable<GoldenCase> Cases()
        {
            yield return GoldenCase.For("primitives-default", GoldenCorpus.BuildPrimitives, Defaults);

            yield return GoldenCase.For("primitives-utf8", GoldenCorpus.BuildPrimitives, () => With(p => p.UseUnicodeStrings = false));

            yield return GoldenCase.For("primitives-no-extensions", GoldenCorpus.BuildPrimitives, () => With(p => p.UseExtensions = false));

            yield return GoldenCase.For("primitives-nulls-serialized", GoldenCorpus.BuildPrimitivesWithNulls, () => With(p => p.SerializeNulls = true));

            yield return GoldenCase.For("primitives-nulls-dropped", GoldenCorpus.BuildPrimitivesWithNulls, Defaults);

            yield return GoldenCase.For("invoice-global-types", GoldenCorpus.BuildInvoice, Defaults);

            yield return GoldenCase.For("invoice-no-global-types", GoldenCorpus.BuildInvoice, () => With(p => p.UsingGlobalTypes = false));

            yield return GoldenCase.For("shapes-polymorphic", GoldenCorpus.BuildShapes, Defaults);

            // The $i back-reference branch. WriteObject keys _cirobj on object IDENTITY and emits
            // {$i: index} for any REPEATED reference, not only for a true cycle, so the index
            // depends on traversal order across the whole document. Every other fixture is a tree
            // of distinct instances and never reaches that branch, which left the most
            // order-sensitive part of the format unpinned.
            yield return GoldenCase.For("shared-reference", GoldenCorpus.BuildSharedReference, Defaults);

            yield return GoldenCase.For("int-array-typed", GoldenCorpus.BuildIntArray, Defaults);

            // Bytes only: with UseTypedArrays off, a root-level array carries no type information,
            // so the reader can only produce List<object>. That is the documented consequence of
            // the flag rather than a defect, and the bytes are still worth pinning.
            yield return GoldenCase.For(
                "int-array-untyped",
                GoldenCorpus.BuildIntArray,
                () => With(p => p.UseTypedArrays = false),
                bytesOnlyReason: "UseTypedArrays off leaves a root array with no type to restore to"
            );

            yield return GoldenCase.For("string-array-typed-v14", GoldenCorpus.BuildStringArray, () => With(p => p.v1_4TypedArray = true));

            yield return GoldenCase.For("string-array-typed", GoldenCorpus.BuildStringArray, Defaults);

            yield return GoldenCase.For("dictionary-string-key", GoldenCorpus.BuildStringKeyedDictionary, Defaults);

            yield return GoldenCase.For("dictionary-int-key", GoldenCorpus.BuildIntKeyedDictionary, Defaults);

            // Was bytes only until the UseUTCDateTime read path stopped converting: write sent the
            // value through ToUniversalTime and read sent it through ToLocalTime, so the restored
            // value depended on the reading machine's time zone even though the bytes did not. The
            // read-back is asserted for real now, on every platform the matrix runs.
            yield return GoldenCase.For("utc-datetime", GoldenCorpus.BuildUtcClock, () => With(p => p.UseUTCDateTime = true));
        }

        /// <summary>
        /// Guards the one identity baked into the fixtures that the csproj cannot pin.
        /// </summary>
        [Test]
        public void Fixtures_RuntimeVersion_MatchesWhatTheyWereGeneratedWith()
        {
            Version? runtime = typeof(int).Assembly.GetName().Version;

            runtime
                .Should()
                .Be(
                    ExpectedRuntimeVersion,
                    "the typed-array fixtures embed '{0}, Version={1}' in their element type's AssemblyQualifiedName, so a "
                        + "TargetFramework bump rewrites them with no serializer change. Regenerate them deliberately and "
                        + "update ExpectedRuntimeVersion in the same commit",
                    CoreLibraryName,
                    ExpectedRuntimeVersion
                );
        }

        [TestCaseSource(nameof(Cases))]
        public void Serialize_MatchesCommittedBytes(GoldenCase testCase)
        {
            BJSON.ClearReflectionCache();
            byte[] actual = BJSON.ToBJSON(testCase.Build(), testCase.Parameters());

            string path = FixturePath(testCase.Name);

            if (Regenerating)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllBytes(path, actual);
                Assert.Fail("Regenerated " + testCase.Name + ". Unset " + RegenerateVariable + " and review the diff.");
                return;
            }

            if (!File.Exists(path))
            {
                Assert.Fail("No golden file for '" + testCase.Name + "' at " + path + ". Run with " + RegenerateVariable + "=1 to create it.");
                return;
            }

            byte[] expected = File.ReadAllBytes(path);
            int difference = FirstDifference(expected, actual);

            // Describe() is built only when it is going to be used. As a `because` argument it was
            // rendered eagerly on every passing case too.
            if (difference >= 0)
            {
                Assert.Fail(Describe(testCase.Name, expected, actual, difference));
            }
        }

        [TestCaseSource(nameof(Cases))]
        public void Deserialize_CommittedBytes_RestoresValue(GoldenCase testCase)
        {
            if (Regenerating)
            {
                Assert.Ignore("Regenerating: the committed bytes are being rewritten, so there is nothing stable to read back.");
                return;
            }

            if (testCase.BytesOnlyReason != null)
            {
                Assert.Ignore("Pins bytes only: " + testCase.BytesOnlyReason + ".");
                return;
            }

            string path = FixturePath(testCase.Name);

            // Fail, not Ignore. The same condition fails in Serialize_MatchesCommittedBytes, and a
            // silent skip here would let this half of the suite erode to nothing - a dropped
            // directory or a case renamed without renaming its file turns cases into skips, and no
            // standard CI check gates a skip count.
            if (!File.Exists(path))
            {
                Assert.Fail("No golden file for '" + testCase.Name + "' at " + path + ". Run with " + RegenerateVariable + "=1 to create it.");
                return;
            }

            BJSON.ClearReflectionCache();
            object restored = testCase.Deserialize(File.ReadAllBytes(path), testCase.Parameters());

            // WithStrictOrdering is load-bearing. BeEquivalentTo ignores collection order by
            // default, so without it a change that reversed the elements of an array, of
            // Invoice.Lines or of ShapeBox.Shapes compared equal and only the serialize half
            // noticed. Verified by reversing GoldenCorpus.BuildIntArray: red with it, green
            // without.
            //
            // A restored-type assertion was considered here and deliberately left out: the
            // read-back goes through ToObject<T>, which casts, so a graph that decodes to the
            // wrong shape throws InvalidCastException before any assertion runs. Checking the
            // type afterwards could never fail.
            restored.Should().BeEquivalentTo(testCase.Build(), options => options.PreferringRuntimeMemberTypes().WithStrictOrdering());
        }

        /// <summary>
        /// Pins what the $i back-reference actually means on read-back: one restored instance, not
        /// two equal copies.
        /// </summary>
        /// <remarks>
        /// The structural comparison in <see cref="Deserialize_CommittedBytes_RestoresValue"/>
        /// cannot see this. Two separately allocated Party instances with identical contents pass
        /// it, so a regression that dropped reference tracking and emitted the object twice would
        /// still produce an equivalent graph.
        /// </remarks>
        [Test]
        public void SharedReference_CommittedBytes_RestoreASingleSharedInstance()
        {
            string path = FixturePath("shared-reference");
            if (!File.Exists(path))
            {
                Assert.Fail("No golden file for 'shared-reference' at " + path + ". Run with " + RegenerateVariable + "=1 to create it.");
                return;
            }

            BJSON.ClearReflectionCache();
            ReferenceBox restored = BJSON.ToObject<ReferenceBox>(File.ReadAllBytes(path), Defaults())!;

            restored.Third.Should().BeSameAs(restored.First, "the two fields were the same instance when written, and $i encodes that");
            restored.Second.Should().NotBeSameAs(restored.First, "the middle value was a distinct instance");
            restored.Second.Name.Should().Be("Other", "the back-reference index must not have been resolved to the wrong object");
        }

        /// <summary>
        /// Reads bytes in which an equal struct was written as a back-reference to the first one.
        /// </summary>
        /// <remarks>
        /// The writer used to look up the objects it had written by Equals, so the second of two equal
        /// structs went out as {"$i": 2}. It compares by identity now and writes both in full, so
        /// nothing produces these bytes any more - but stored payloads contain them, and both read
        /// paths must resolve the reference to the first struct's value. The file was written once
        /// by the writer before that fix (a3849df). It is read-only: it is not one of
        /// <see cref="Cases"/>, so a regeneration run does not rewrite it.
        /// </remarks>
        [TestCase(true)]
        [TestCase(false)]
        public void LegacyEqualStructs_CommittedBytes_RestoreBothValues(bool oneStep)
        {
            string path = FixturePath(LegacyEqualStructs);
            if (!File.Exists(path))
            {
                Assert.Fail("No legacy file '" + LegacyEqualStructs + "' at " + path + ". It cannot be regenerated: the current writer no longer produces it.");
                return;
            }

            byte[] bytes = File.ReadAllBytes(path);
            // Guards the premise, so the test cannot pass on a file that has lost its back-reference.
            bytes
                .AsSpan()
                .IndexOf(Encoding.Unicode.GetBytes("$i"))
                .Should()
                .BeGreaterThanOrEqualTo(0, "the file has to hold the back-reference this test exists to read");

            BJSON.ClearReflectionCache();
            EqStructsThenShared restored = new Deserializer(Defaults()) { OneStep = oneStep }.ToObject<EqStructsThenShared>(bytes)!;

            restored.A.Should().Be(new EqPoint { X = 5, Y = 6 });
            restored.B.Should().Be(new EqPoint { X = 5, Y = 6 });
        }

        private static BJSONParameters Defaults()
        {
            return new BJSONParameters();
        }

        private static BJSONParameters With(Action<BJSONParameters> configure)
        {
            BJSONParameters parameters = new BJSONParameters();
            configure(parameters);
            return parameters;
        }

        /// <summary>
        /// Returns the index of the first differing byte, or -1 when the two arrays are identical.
        /// </summary>
        private static int FirstDifference(byte[] expected, byte[] actual)
        {
            int shared = Math.Min(expected.Length, actual.Length);
            for (int i = 0; i < shared; i++)
            {
                if (expected[i] != actual[i])
                {
                    return i;
                }
            }

            return expected.Length == actual.Length ? -1 : shared;
        }

        private static string Describe(string name, byte[] expected, byte[] actual, int difference)
        {
            StringBuilder message = new StringBuilder();
            message.AppendLine("Wire format changed for '" + name + "'.");
            message.AppendLine("first difference at byte " + difference + ", marked with '>' below.");
            message.AppendLine("expected " + expected.Length + " bytes: " + Hex(expected, difference));
            message.AppendLine("actual   " + actual.Length + " bytes: " + Hex(actual, difference));
            message.Append("If this change is intended, regenerate with " + RegenerateVariable + "=1 and review the diff.");
            return message.ToString();
        }

        /// <summary>
        /// Renders a window of bytes centred on <paramref name="centre"/>.
        /// </summary>
        /// <remarks>
        /// Centring is the point. A fixed head-of-array window renders identically for both arrays
        /// whenever the difference falls outside it, which is the common case: most fixtures here
        /// are larger than any window worth printing.
        /// </remarks>
        private static string Hex(byte[] bytes, int centre)
        {
            const int window = 24;

            int start = Math.Max(0, centre - window);
            int end = Math.Min(bytes.Length, centre + window + 1);

            StringBuilder text = new StringBuilder();
            if (start > 0)
            {
                text.Append("... ");
            }

            for (int i = start; i < end; i++)
            {
                if (i == centre)
                {
                    text.Append('>');
                }

                text.Append(bytes[i].ToString("x2", CultureInfo.InvariantCulture));
                text.Append(' ');
            }

            if (end < bytes.Length)
            {
                text.Append("... (" + (bytes.Length - end) + " more)");
            }

            if (centre >= bytes.Length)
            {
                text.Append("[ends at " + bytes.Length + "]");
            }

            return text.ToString().TrimEnd();
        }

        private static string FixturePath(string name)
        {
            return Path.Combine(FixtureDirectory(), name + ".bjson");
        }

        private static string FixtureDirectory()
        {
            DirectoryInfo? directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "FastBinaryJson.slnx")))
            {
                directory = directory.Parent;
            }

            if (directory == null)
            {
                throw new DirectoryNotFoundException(
                    "Could not locate the repository root (no FastBinaryJson.slnx above " + TestContext.CurrentContext.TestDirectory + ")."
                );
            }

            return Path.Combine(directory.FullName, "tests", "FastBinaryJson.UnitTests", "Golden", "Fixtures");
        }
    }
}
