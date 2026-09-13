using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

using AwesomeAssertions;

using fastBinaryJSON;

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
     * Two couplings are deliberate and load-bearing:
     *
     *   1. The fixtures embed each type's AssemblyQualifiedName in $type, which carries this
     *      assembly's NAME and VERSION. Both are pinned in the csproj for exactly that reason.
     *      Renaming this project, or bumping its assembly version, invalidates every fixture.
     *
     *   2. Field order in the output follows reflection's member order for each fixture type.
     *      Reordering properties in GoldenCorpus.cs is a format change, not a cosmetic edit.
     *
     * Regenerating: set FBJ_REGEN_GOLDEN=1 and run the suite. That REWRITES the committed bytes,
     * so only do it when a format change is intended, and review the resulting diff as carefully
     * as any source change. A regeneration run always fails at the end so it can never be mistaken
     * for a passing build.
     */
    [TestFixture]
    public sealed class GoldenFileTests
    {
        private const string RegenerateVariable = "FBJ_REGEN_GOLDEN";

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

            yield return GoldenCase.For("int-array-typed", GoldenCorpus.BuildIntArray, Defaults);

            // Bytes only: with UseTypedArrays off, a root-level array carries no type information,
            // so the reader can only produce List<object>. That is the documented consequence of
            // the flag rather than a defect, and the bytes are still worth pinning.
            yield return GoldenCase.For(
                "int-array-untyped",
                GoldenCorpus.BuildIntArray,
                () => With(p => p.UseTypedArrays = false),
                bytesOnlyReason: "UseTypedArrays off leaves a root array with no type to restore to");

            yield return GoldenCase.For("string-array-typed-v14", GoldenCorpus.BuildStringArray, () => With(p => p.v1_4TypedArray = true));

            yield return GoldenCase.For("string-array-typed", GoldenCorpus.BuildStringArray, Defaults);

            yield return GoldenCase.For("dictionary-string-key", GoldenCorpus.BuildStringKeyedDictionary, Defaults);

            yield return GoldenCase.For("dictionary-int-key", GoldenCorpus.BuildIntKeyedDictionary, Defaults);

            // Bytes only: UseUTCDateTime routes the value through ToUniversalTime on write and
            // ToLocalTime on read, so the round-tripped value depends on the machine's time zone
            // even though the bytes do not. The asymmetry is pinned here rather than papered over.
            yield return GoldenCase.For(
                "utc-datetime",
                GoldenCorpus.BuildUtcClock,
                () => With(p => p.UseUTCDateTime = true),
                bytesOnlyReason: "round-tripped value is time-zone dependent");
        }

        [TestCaseSource(nameof(Cases))]
        public void Serialize_MatchesCommittedBytes(GoldenCase testCase)
        {
            BJSON.ClearReflectionCache();
            byte[] actual = BJSON.ToBJSON(testCase.Build(), testCase.Parameters());

            string path = FixturePath(testCase.Name);

            if (Regenerating)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllBytes(path, actual);
                Assert.Fail("Regenerated " + testCase.Name + ". Unset " + RegenerateVariable + " and review the diff.");
                return;
            }

            if (File.Exists(path) == false)
            {
                Assert.Fail("No golden file for '" + testCase.Name + "' at " + path + ". Run with " + RegenerateVariable + "=1 to create it.");
                return;
            }

            byte[] expected = File.ReadAllBytes(path);
            actual.Should().Equal(expected, Describe(testCase.Name, expected, actual));
        }

        [TestCaseSource(nameof(Cases))]
        public void Deserialize_CommittedBytes_RestoresValue(GoldenCase testCase)
        {
            if (testCase.BytesOnlyReason != null)
            {
                Assert.Ignore("Pins bytes only: " + testCase.BytesOnlyReason + ".");
                return;
            }

            string path = FixturePath(testCase.Name);
            if (File.Exists(path) == false)
            {
                Assert.Ignore("No golden file yet for '" + testCase.Name + "'.");
                return;
            }

            BJSON.ClearReflectionCache();
            object restored = testCase.Deserialize(File.ReadAllBytes(path), testCase.Parameters());

            restored.Should().BeEquivalentTo(testCase.Build(), options => options.PreferringRuntimeMemberTypes());
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

        private static string Describe(string name, byte[] expected, byte[] actual)
        {
            StringBuilder message = new StringBuilder();
            message.AppendLine("Wire format changed for '" + name + "'.");
            message.AppendLine("expected " + expected.Length + " bytes: " + Hex(expected));
            message.AppendLine("actual   " + actual.Length + " bytes: " + Hex(actual));
            message.Append("If this change is intended, regenerate with " + RegenerateVariable + "=1 and review the diff.");
            return message.ToString();
        }

        private static string Hex(byte[] bytes)
        {
            const int limit = 64;
            StringBuilder text = new StringBuilder();
            for (int i = 0; i < bytes.Length && i < limit; i++)
            {
                text.Append(bytes[i].ToString("x2"));
                text.Append(' ');
            }

            if (bytes.Length > limit)
            {
                text.Append("... (" + (bytes.Length - limit) + " more)");
            }

            return text.ToString().TrimEnd();
        }

        private static string FixturePath(string name)
        {
            return Path.Combine(FixtureDirectory(), name + ".bjson");
        }

        private static string FixtureDirectory()
        {
            DirectoryInfo directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (directory != null && File.Exists(Path.Combine(directory.FullName, "FastBinaryJson.slnx")) == false)
            {
                directory = directory.Parent;
            }

            if (directory == null)
            {
                throw new DirectoryNotFoundException(
                    "Could not locate the repository root (no FastBinaryJson.slnx above " + TestContext.CurrentContext.TestDirectory + ").");
            }

            return Path.Combine(directory.FullName, "tests", "FastBinaryJson.UnitTests", "Golden", "Fixtures");
        }
    }
}
