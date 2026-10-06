using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FastBinaryJson.Benchmarks.Corpus;

namespace FastBinaryJson.Benchmarks
{
    /// <summary>
    /// A single corpus payload, type-erased so reports can iterate the corpus uniformly while
    /// each case still dispatches its own generic serialize/deserialize calls.
    /// </summary>
    internal interface IPayloadCase
    {
        string Name { get; }

        string Shape { get; }

        byte[] Serialize(ISerializerArm arm);

        object Deserialize(ISerializerArm arm, byte[] bytes);

        /// <summary>
        /// Serializes, deserializes, then re-serializes and compares bytes. Byte equality across
        /// the round trip is a stronger check than deep equality and needs no per-type comparer -
        /// anything the serializer dropped or widened changed the second encoding.
        /// </summary>
        bool TryRoundTrip(ISerializerArm arm, out string failure);
    }

    internal sealed class PayloadCase<T> : IPayloadCase
    {
        private readonly string _name;
        private readonly string _shape;
        private readonly T _value;

        public PayloadCase(string name, string shape, T value)
        {
            _name = name;
            _shape = shape;
            _value = value;
        }

        public string Name => _name;

        public string Shape => _shape;

        public T Value => _value;

        public byte[] Serialize(ISerializerArm arm) => arm.Serialize(_value);

        public object Deserialize(ISerializerArm arm, byte[] bytes) => arm.Deserialize<T>(bytes)!;

        public bool TryRoundTrip(ISerializerArm arm, out string failure)
        {
            try
            {
                byte[] first = arm.Serialize(_value);
                T restored = arm.Deserialize<T>(first);
                byte[] second = arm.Serialize(restored);

                if (first.Length != second.Length)
                {
                    failure = string.Concat(
                        "re-encode differs in length: ",
                        first.Length.ToString(CultureInfo.InvariantCulture),
                        " then ",
                        second.Length.ToString(CultureInfo.InvariantCulture)
                    );
                    return false;
                }

                for (int i = 0; i < first.Length; i++)
                {
                    if (first[i] != second[i])
                    {
                        failure = string.Concat("re-encode differs at byte ", i.ToString(CultureInfo.InvariantCulture));
                        return false;
                    }
                }

                failure = string.Empty;
                return true;
            }
#pragma warning disable CA1031 // A round trip that throws is reported as a failure of that arm, not propagated.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                failure = string.Concat(ex.GetType().Name, ": ", ex.Message);
                return false;
            }
        }
    }

    internal static class Payloads
    {
        public static IReadOnlyList<IPayloadCase> All()
        {
            return new List<IPayloadCase>
            {
                new PayloadCase<FlatPrimitives>("FlatPrimitives", "single flat object, every primitive", PayloadFactory.CreateFlatPrimitives()),
                new PayloadCase<Order>("NestedOrder", "nested graph, " + PayloadFactory.OrderLineCount + " lines", PayloadFactory.CreateNestedOrder()),
                new PayloadCase<List<LogEntry>>(
                    "LargeCollection",
                    PayloadFactory.LargeCollectionCount + " homogeneous items",
                    PayloadFactory.CreateLargeCollection()
                ),
                new PayloadCase<List<CorrelationRecord>>(
                    "GuidDense",
                    PayloadFactory.CorrelationCount + " records, 8 GUIDs each",
                    PayloadFactory.CreateCorrelationRecords()
                ),
                new PayloadCase<ShapeCatalogue>("Polymorphic", PayloadFactory.ShapeCount + " shapes, 3 derived types", PayloadFactory.CreateShapeCatalogue()),
                new PayloadCase<CharHolder>("CharHolder", "known defect probe, not a shape", PayloadFactory.CreateCharHolder()),
                new PayloadCase<Dictionary<string, string>>(
                    "LongDictionaryKey",
                    "defect probe: dictionary key over 256 encoded bytes",
                    PayloadFactory.CreateLongKeyDictionary()
                ),
            };
        }

        /// <summary>
        /// Short stable keys used as BenchmarkDotNet parameter values.
        /// </summary>
        public static ISerializerArm ArmByKey(string key)
        {
            switch (key)
            {
                case "fbj-utf16":
                    return new FastBinaryJsonArm(useUnicodeStrings: true);
                case "fbj-utf8":
                    return new FastBinaryJsonArm(useUnicodeStrings: false);
                case "upstream-utf16":
                    return new UpstreamFastBinaryJsonArm(useUnicodeStrings: true);
                case "upstream-utf8":
                    return new UpstreamFastBinaryJsonArm(useUnicodeStrings: false);
                case "stj":
                    return new SystemTextJsonArm();
                case "msgpack":
                    return new MessagePackArm();
                default:
                    throw new ArgumentOutOfRangeException(nameof(key), key, "Unknown serializer arm.");
            }
        }

        public static IPayloadCase ByName(string name)
        {
            IPayloadCase? payload = All().FirstOrDefault(candidate => string.Equals(candidate.Name, name, StringComparison.Ordinal));
            if (payload != null)
            {
                return payload;
            }

            throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown payload.");
        }

        public static IReadOnlyList<ISerializerArm> AllArms()
        {
            return new List<ISerializerArm>
            {
                new FastBinaryJsonArm(useUnicodeStrings: true),
                new FastBinaryJsonArm(useUnicodeStrings: false),
                new UpstreamFastBinaryJsonArm(useUnicodeStrings: true),
                new UpstreamFastBinaryJsonArm(useUnicodeStrings: false),
                new SystemTextJsonArm(),
                new MessagePackArm(),
            };
        }
    }
}
