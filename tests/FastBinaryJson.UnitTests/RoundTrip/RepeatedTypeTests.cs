using System.Collections.Generic;
using AwesomeAssertions;
using DuraIT.FastBinaryJson;
using NUnit.Framework;

namespace FastBinaryJson.UnitTests.RoundTrip
{
    /*
     * A root list is written without global types, so every element carries its full type name. The
     * one-step reader takes an element's $type from the previous element when the bytes are the
     * same, instead of decoding and resolving the name again. Runs on both targets.
     */
    [TestFixture]
    [TestOf(typeof(TypedReader))]
    public sealed class RepeatedTypeTests
    {
        [TestCase(true)]
        [TestCase(false)]
        public void ToObject_RootListWithRuns_TakesRepeatsFromThePreviousElement(bool unicode)
        {
            BJSONParameters parameters = new BJSONParameters { UseUnicodeStrings = unicode };
            List<RepeatBase> value = new List<RepeatBase>
            {
                new RepeatA { A = 1 },
                new RepeatA { A = 2 },
                new RepeatB { B = 3 },
                new RepeatB { B = 4 },
                new RepeatA { A = 5 },
            };
            Deserializer deserializer = new Deserializer(parameters);

            List<RepeatBase> restored = deserializer.ToObject<List<RepeatBase>>(BJSON.ToBJSON(value, parameters))!;

            restored.Should().BeEquivalentTo(value, o => o.PreferringRuntimeMemberTypes().WithStrictOrdering());
            deserializer.TypesRepeated.Should().Be(2);
        }

        /// <summary>
        /// RepeatA and RepeatB have names of the same length that differ in one character, so only the
        /// content can tell them apart.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        public void ToObject_AlternatingTypesOfEqualNameLength_ResolvesEachElement(bool unicode)
        {
            BJSONParameters parameters = new BJSONParameters { UseUnicodeStrings = unicode };
            List<RepeatBase> value = new List<RepeatBase>
            {
                new RepeatA { A = 1 },
                new RepeatB { B = 2 },
                new RepeatA { A = 3 },
                new RepeatB { B = 4 },
            };
            Deserializer deserializer = new Deserializer(parameters);

            List<RepeatBase> restored = deserializer.ToObject<List<RepeatBase>>(BJSON.ToBJSON(value, parameters))!;

            typeof(RepeatA).AssemblyQualifiedName!.Length.Should().Be(typeof(RepeatB).AssemblyQualifiedName!.Length);
            restored.Should().BeEquivalentTo(value, o => o.PreferringRuntimeMemberTypes().WithStrictOrdering());
            deserializer.TypesRepeated.Should().Be(0);
        }
    }

    public abstract class RepeatBase { }

    public sealed class RepeatA : RepeatBase
    {
        public int A { get; set; }
    }

    public sealed class RepeatB : RepeatBase
    {
        public int B { get; set; }
    }
}
