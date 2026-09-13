using System;
using System.Collections.Generic;

using fastBinaryJSON;

namespace FastBinaryJson.UnitTests.Golden
{
    /*
     * Deterministic fixtures for the golden wire-format files.
     *
     * Nothing here may read the clock, the machine's time zone, a random source, or anything else
     * that varies between runs or machines. Every DateTime is a fixed literal, every Guid is built
     * from fixed bytes, and collection order is fixed. If a value in this file changes, every
     * affected golden file changes with it, which is a deliberate format change and not a refresh.
     */
    internal static class GoldenCorpus
    {
        internal static readonly DateTime FixedLocal = new DateTime(2026, 1, 2, 3, 4, 5, 678, DateTimeKind.Unspecified);
        internal static readonly DateTime FixedUtc = new DateTime(2026, 1, 2, 3, 4, 5, 678, DateTimeKind.Utc);

        internal static Guid FixedGuid(byte seed)
        {
            byte[] bytes = new byte[16];
            for (int i = 0; i < 16; i++)
            {
                bytes[i] = (byte)(seed + i);
            }

            return new Guid(bytes);
        }

        internal static Primitives BuildPrimitives()
        {
            return new Primitives
            {
                Bool = true,
                ByteValue = 200,
                ShortValue = -12345,
                UShortValue = 54321,
                IntValue = -1234567,
                UIntValue = 3234567890,
                LongValue = -1234567890123L,
                ULongValue = 12345678901234567890UL,
                FloatValue = 1.5f,
                DoubleValue = -2.25d,
                DecimalValue = 12345.6789m,
                StringValue = "ascii and éèê and متن",
                Timestamp = FixedLocal,
                Duration = new TimeSpan(1, 2, 3, 4, 5),
                Id = FixedGuid(1),
                Blob = new byte[] { 0, 1, 2, 253, 254, 255 },
                Flavour = Flavour.Salty,
            };
        }

        internal static Primitives BuildPrimitivesWithNulls()
        {
            Primitives p = BuildPrimitives();
            p.StringValue = null;
            p.Blob = null;
            return p;
        }

        internal static Invoice BuildInvoice()
        {
            return new Invoice
            {
                Number = "INV-000123",
                Issued = FixedLocal,
                BillTo = new Party { Name = "Acme BV", City = "Heerlen" },
                ShipTo = new Party { Name = "Acme Warehouse", City = "Kerkrade" },
                Lines = new List<InvoiceLine>
                {
                    new InvoiceLine { Sku = "A-1", Quantity = 2, UnitPrice = 9.95m },
                    new InvoiceLine { Sku = "B-2", Quantity = 10, UnitPrice = 1.05m },
                    new InvoiceLine { Sku = "C-3", Quantity = 1, UnitPrice = 249.00m },
                },
            };
        }

        internal static ShapeBox BuildShapes()
        {
            return new ShapeBox
            {
                Shapes = new List<Shape>
                {
                    new Circle { Label = "c1", Radius = 2.5d },
                    new Rectangle { Label = "r1", Width = 3d, Height = 4d },
                    new Circle { Label = "c2", Radius = 0.5d },
                },
            };
        }

        internal static int[] BuildIntArray()
        {
            return new int[] { 1, 2, 3, 5, 8, 13, 21 };
        }

        internal static string[] BuildStringArray()
        {
            return new string[] { "alpha", "beta", "gamma" };
        }

        internal static Dictionary<string, Party> BuildStringKeyedDictionary()
        {
            return new Dictionary<string, Party>
            {
                { "first", new Party { Name = "One", City = "Landgraaf" } },
                { "second", new Party { Name = "Two", City = "Brunssum" } },
            };
        }

        internal static Dictionary<int, Party> BuildIntKeyedDictionary()
        {
            return new Dictionary<int, Party>
            {
                { 10, new Party { Name = "Ten", City = "Landgraaf" } },
                { 20, new Party { Name = "Twenty", City = "Brunssum" } },
            };
        }

        internal static Clock BuildUtcClock()
        {
            return new Clock { Moment = FixedUtc };
        }
    }

    internal enum Flavour
    {
        Sweet = 0,
        Salty = 1,
        Sour = 2,
    }

    /*
     * char, sbyte and DateTimeOffset are deliberately absent.
     *
     * char and sbyte serialize correctly but cannot be read back into a property of their own
     * type; DateTimeOffset cannot be serialized at all. All three are pinned by KnownDefectTests
     * instead, so that this corpus stays a record of the format as it actually works. Add them
     * here as part of whichever change fixes them.
     */
    internal sealed class Primitives
    {
        public bool Bool { get; set; }

        public byte ByteValue { get; set; }

        public short ShortValue { get; set; }

        public ushort UShortValue { get; set; }

        public int IntValue { get; set; }

        public uint UIntValue { get; set; }

        public long LongValue { get; set; }

        public ulong ULongValue { get; set; }

        public float FloatValue { get; set; }

        public double DoubleValue { get; set; }

        public decimal DecimalValue { get; set; }

        public string StringValue { get; set; }

        public DateTime Timestamp { get; set; }

        public TimeSpan Duration { get; set; }

        public Guid Id { get; set; }

        public byte[] Blob { get; set; }

        public Flavour Flavour { get; set; }
    }

    internal sealed class Party
    {
        public string Name { get; set; }

        public string City { get; set; }
    }

    internal sealed class InvoiceLine
    {
        public string Sku { get; set; }

        public int Quantity { get; set; }

        public decimal UnitPrice { get; set; }
    }

    internal sealed class Invoice
    {
        public string Number { get; set; }

        public DateTime Issued { get; set; }

        public Party BillTo { get; set; }

        public Party ShipTo { get; set; }

        public List<InvoiceLine> Lines { get; set; }
    }

    internal abstract class Shape
    {
        public string Label { get; set; }
    }

    internal sealed class Circle : Shape
    {
        public double Radius { get; set; }
    }

    internal sealed class Rectangle : Shape
    {
        public double Width { get; set; }

        public double Height { get; set; }
    }

    internal sealed class ShapeBox
    {
        public List<Shape> Shapes { get; set; }
    }

    internal sealed class Clock
    {
        public DateTime Moment { get; set; }
    }
}
