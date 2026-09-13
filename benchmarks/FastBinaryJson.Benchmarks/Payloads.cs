using System;
using System.Collections.Generic;
using FastBinaryJson.Benchmarks.Corpus;

namespace FastBinaryJson.Benchmarks
{
    /// <summary>
    /// A single corpus payload, type-erased so reports can iterate the corpus uniformly while
    /// each case still dispatches its own generic serialize/deserialize calls.
    /// </summary>
    public abstract class PayloadCase
    {
        public abstract string Name { get; }

        public abstract string Shape { get; }

        public abstract byte[] Serialize(ISerializerArm arm);

        public abstract object Deserialize(ISerializerArm arm, byte[] bytes);

        /// <summary>
        /// Serializes, deserializes, then re-serializes and compares bytes. Byte equality across
        /// the round trip is a stronger check than deep equality and needs no per-type comparer -
        /// anything the serializer dropped or widened changes the second encoding.
        /// </summary>
        public abstract bool TryRoundTrip(ISerializerArm arm, out string error);
    }

    public sealed class PayloadCase<T> : PayloadCase
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

        public override string Name => _name;

        public override string Shape => _shape;

        public T Value => _value;

        public override byte[] Serialize(ISerializerArm arm) => arm.Serialize(_value);

        public override object Deserialize(ISerializerArm arm, byte[] bytes) => arm.Deserialize<T>(bytes)!;

        public override bool TryRoundTrip(ISerializerArm arm, out string error)
        {
            try
            {
                byte[] first = arm.Serialize(_value);
                T restored = arm.Deserialize<T>(first);
                byte[] second = arm.Serialize(restored);

                if (first.Length != second.Length)
                {
                    error = string.Concat("re-encode differs in length: ", first.Length.ToString(), " then ", second.Length.ToString());
                    return false;
                }

                for (int i = 0; i < first.Length; i++)
                {
                    if (first[i] != second[i])
                    {
                        error = string.Concat("re-encode differs at byte ", i.ToString());
                        return false;
                    }
                }

                error = string.Empty;
                return true;
            }
            catch (Exception ex)
            {
                error = string.Concat(ex.GetType().Name, ": ", ex.Message);
                return false;
            }
        }
    }

    public static class Payloads
    {
        public static IReadOnlyList<PayloadCase> All()
        {
            return new List<PayloadCase>
            {
                new PayloadCase<FlatPrimitives>("FlatPrimitives", "single flat object, every primitive", PayloadFactory.CreateFlatPrimitives()),
                new PayloadCase<Order>("NestedOrder", "nested graph, " + PayloadFactory.OrderLineCount + " lines", PayloadFactory.CreateNestedOrder()),
                new PayloadCase<List<LogEntry>>("LargeCollection", PayloadFactory.LargeCollectionCount + " homogeneous items", PayloadFactory.CreateLargeCollection()),
                new PayloadCase<List<CorrelationRecord>>("GuidDense", PayloadFactory.CorrelationCount + " records, 8 GUIDs each", PayloadFactory.CreateCorrelationRecords()),
                new PayloadCase<ShapeCatalogue>("Polymorphic", PayloadFactory.ShapeCount + " shapes, 3 derived types", PayloadFactory.CreateShapeCatalogue()),
                new PayloadCase<CharHolder>("CharHolder", "known defect probe, not a shape", PayloadFactory.CreateCharHolder()),
                new PayloadCase<Dictionary<string, string>>("LongDictionaryKey", "defect probe: dictionary key over 256 encoded bytes", PayloadFactory.CreateLongKeyDictionary()),
            };
        }

        /// <summary>
        /// Short stable keys used as BenchmarkDotNet parameter values.
        /// </summary>
        public static ISerializerArm ArmByKey(string key)
        {
            switch (key)
            {
                case "fbj-utf16": return new FastBinaryJsonArm(useUnicodeStrings: true);
                case "fbj-utf8": return new FastBinaryJsonArm(useUnicodeStrings: false);
                case "stj": return new SystemTextJsonArm();
                case "msgpack": return new MessagePackArm();
                default: throw new ArgumentOutOfRangeException(nameof(key), key, "Unknown serializer arm.");
            }
        }

        public static PayloadCase ByName(string name)
        {
            foreach (PayloadCase payload in All())
            {
                if (string.Equals(payload.Name, name, StringComparison.Ordinal))
                {
                    return payload;
                }
            }

            throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown payload.");
        }

        public static IReadOnlyList<ISerializerArm> AllArms()
        {
            return new List<ISerializerArm>
            {
                new FastBinaryJsonArm(useUnicodeStrings: true),
                new FastBinaryJsonArm(useUnicodeStrings: false),
                new SystemTextJsonArm(),
                new MessagePackArm(),
            };
        }
    }
}
