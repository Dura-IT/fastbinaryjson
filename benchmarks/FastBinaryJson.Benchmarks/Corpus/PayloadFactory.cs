using System;
using System.Collections.Generic;

namespace FastBinaryJson.Benchmarks.Corpus
{
    /// <summary>
    /// Builds the benchmark corpus deterministically from a fixed seed, so every run on every
    /// machine measures byte-identical input. All content is generated here; nothing is read
    /// from disk or sourced externally.
    /// </summary>
    public static class PayloadFactory
    {
        private const int Seed = 20260913;
        private static readonly DateTime BaseUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        private static readonly string[] Words =
        {
            "alpha", "bravo", "charlie", "delta", "echo", "foxtrot", "golf", "hotel",
            "india", "juliet", "kilo", "lima", "mike", "november", "oscar", "papa",
        };

        public const int LargeCollectionCount = 1000;
        public const int CorrelationCount = 200;
        public const int ShapeCount = 300;
        public const int OrderLineCount = 25;

        public static FlatPrimitives CreateFlatPrimitives()
        {
            Random random = NewRandom();

            return new FlatPrimitives
            {
                Int32Value = random.Next(int.MinValue, int.MaxValue),
                Int64Value = NextInt64(random),
                Int16Value = (short)random.Next(short.MinValue, short.MaxValue),
                ByteValue = (byte)random.Next(0, 256),
                BoolValue = true,
                DoubleValue = random.NextDouble() * 1e6,
                SingleValue = (float)(random.NextDouble() * 1e3),
                DecimalValue = new decimal(random.NextDouble() * 1e4),
                StringValue = NextSentence(random, 8),
                DateTimeValue = BaseUtc.AddSeconds(random.Next(0, 31_536_000)),
                TimeSpanValue = TimeSpan.FromMilliseconds(random.Next(0, 86_400_000)),
                GuidValue = NextGuid(random),
                NullableIntValue = random.Next(0, 1000),
            };
        }

        public static CharHolder CreateCharHolder()
        {
            return new CharHolder { Value = 'x', Context = "isolates the ParseChar defect" };
        }

        /// <summary>
        /// Probes a suspected defect in WriteName, which writes the encoded name length into a
        /// single byte and then emits only (length % 256) bytes. Any name at or above 256 encoded
        /// bytes is therefore truncated, and at exactly 256 it writes a zero length and no bytes.
        /// Property names that long are unrealistic, but WriteName is also the path taken by
        /// string dictionary keys, where a long composite key is ordinary.
        /// </summary>
        public static Dictionary<string, string> CreateLongKeyDictionary()
        {
            Random random = NewRandom();
            string longKey = NextSentence(random, 60);

            return new Dictionary<string, string>
            {
                { "short", "ordinary value" },
                { longKey, "value behind a key of " + longKey.Length.ToString() + " characters" },
            };
        }

        public static Order CreateNestedOrder()
        {
            Random random = NewRandom();
            List<OrderLine> lines = new List<OrderLine>(OrderLineCount);
            decimal total = 0m;

            for (int i = 0; i < OrderLineCount; i++)
            {
                decimal unitPrice = new decimal(Math.Round(random.NextDouble() * 500.0, 2));
                int quantity = random.Next(1, 20);
                total += unitPrice * quantity;

                lines.Add(new OrderLine
                {
                    LineNumber = i + 1,
                    Sku = string.Concat("SKU-", random.Next(100000, 999999).ToString()),
                    Description = NextSentence(random, 6),
                    Quantity = quantity,
                    UnitPrice = unitPrice,
                });
            }

            return new Order
            {
                OrderId = NextGuid(random),
                PlacedUtc = BaseUtc.AddSeconds(random.Next(0, 31_536_000)),
                Reference = NextSentence(random, 3),
                Total = total,
                Customer = new Customer
                {
                    CustomerId = NextGuid(random),
                    DisplayName = NextSentence(random, 2),
                    Email = string.Concat(Words[random.Next(Words.Length)], "@example.invalid"),
                    Tags = new List<string> { Words[random.Next(Words.Length)], Words[random.Next(Words.Length)] },
                    Billing = CreateAddress(random),
                    Shipping = CreateAddress(random),
                },
                Lines = lines,
            };
        }

        public static List<LogEntry> CreateLargeCollection()
        {
            Random random = NewRandom();
            List<LogEntry> entries = new List<LogEntry>(LargeCollectionCount);

            for (int i = 0; i < LargeCollectionCount; i++)
            {
                entries.Add(new LogEntry
                {
                    Sequence = i,
                    TimestampUtc = BaseUtc.AddMilliseconds(i * 37),
                    Level = random.Next(0, 6),
                    Source = Words[random.Next(Words.Length)],
                    Message = NextSentence(random, 10),
                    Succeeded = random.Next(0, 10) > 2,
                    DurationMs = random.NextDouble() * 250.0,
                });
            }

            return entries;
        }

        public static List<CorrelationRecord> CreateCorrelationRecords()
        {
            Random random = NewRandom();
            List<CorrelationRecord> records = new List<CorrelationRecord>(CorrelationCount);

            for (int i = 0; i < CorrelationCount; i++)
            {
                records.Add(new CorrelationRecord
                {
                    Id = NextGuid(random),
                    TraceId = NextGuid(random),
                    SpanId = NextGuid(random),
                    ParentSpanId = NextGuid(random),
                    TenantId = NextGuid(random),
                    SessionId = NextGuid(random),
                    RequestId = NextGuid(random),
                    CorrelationId = NextGuid(random),
                    Sequence = i,
                });
            }

            return records;
        }

        public static ShapeCatalogue CreateShapeCatalogue()
        {
            Random random = NewRandom();
            List<Shape> shapes = new List<Shape>(ShapeCount);

            for (int i = 0; i < ShapeCount; i++)
            {
                string label = Words[random.Next(Words.Length)];

                switch (i % 3)
                {
                    case 0:
                        shapes.Add(new Circle { Id = i, Label = label, Radius = random.NextDouble() * 100.0 });
                        break;
                    case 1:
                        shapes.Add(new Rectangle
                        {
                            Id = i,
                            Label = label,
                            Width = random.NextDouble() * 100.0,
                            Height = random.NextDouble() * 100.0,
                        });
                        break;
                    default:
                        shapes.Add(new Triangle
                        {
                            Id = i,
                            Label = label,
                            BaseLength = random.NextDouble() * 100.0,
                            Height = random.NextDouble() * 100.0,
                            Skew = random.NextDouble(),
                        });
                        break;
                }
            }

            return new ShapeCatalogue { Name = "benchmark-catalogue", Shapes = shapes };
        }

        private static Address CreateAddress(Random random)
        {
            return new Address
            {
                Line1 = string.Concat(Words[random.Next(Words.Length)], " ", random.Next(1, 300).ToString()),
                Line2 = Words[random.Next(Words.Length)],
                PostalCode = string.Concat(random.Next(1000, 9999).ToString(), " ", Words[random.Next(Words.Length)].Substring(0, 2).ToUpperInvariant()),
                City = Words[random.Next(Words.Length)],
                CountryCode = "NL",
            };
        }

        private static Random NewRandom() => new Random(Seed);

        private static long NextInt64(Random random)
        {
            byte[] buffer = new byte[8];
            random.NextBytes(buffer);
            return BitConverter.ToInt64(buffer, 0);
        }

        private static Guid NextGuid(Random random)
        {
            byte[] buffer = new byte[16];
            random.NextBytes(buffer);
            return new Guid(buffer);
        }

        private static string NextSentence(Random random, int wordCount)
        {
            string[] parts = new string[wordCount];
            for (int i = 0; i < wordCount; i++)
            {
                parts[i] = Words[random.Next(Words.Length)];
            }

            return string.Join(" ", parts);
        }
    }
}
