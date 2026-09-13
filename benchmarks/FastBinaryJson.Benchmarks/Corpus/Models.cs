using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace FastBinaryJson.Benchmarks.Corpus
{
    /*
     * House convention prefers `record` for immutable data models. These are deliberately
     * mutable classes with implicit parameterless constructors instead: fastBinaryJSON is a
     * 2010-era reflection/Reflection.Emit serializer that instantiates via a parameterless
     * constructor and writes through property setters. A positional record exposes neither,
     * so the corpus would fail to round-trip and the benchmark would measure nothing.
     *
     * The corpus is authored here in full. No externally sourced payload, schema or sample
     * data enters this repository - see the evidence rule in the project plan.
     */

    /// <summary>
    /// Every primitive the serializer handles, in one flat object with no nesting.
    /// `char` is deliberately absent - see <see cref="CharHolder"/> for why.
    /// </summary>
    public sealed class FlatPrimitives
    {
        public int Int32Value { get; set; }
        public long Int64Value { get; set; }
        public short Int16Value { get; set; }
        public byte ByteValue { get; set; }
        public bool BoolValue { get; set; }
        public double DoubleValue { get; set; }
        public float SingleValue { get; set; }
        public decimal DecimalValue { get; set; }
        public string StringValue { get; set; } = string.Empty;
        public DateTime DateTimeValue { get; set; }
        public TimeSpan TimeSpanValue { get; set; }
        public Guid GuidValue { get; set; }
        public int? NullableIntValue { get; set; }
    }

    /// <summary>
    /// Deeply nested object graph: order to customer to address, plus a nested line collection.
    /// </summary>
    public sealed class Order
    {
        public Guid OrderId { get; set; }
        public DateTime PlacedUtc { get; set; }
        public string Reference { get; set; } = string.Empty;
        public Customer? Customer { get; set; }
        public List<OrderLine> Lines { get; set; } = new List<OrderLine>();
        public decimal Total { get; set; }
    }

    public sealed class Customer
    {
        public Guid CustomerId { get; set; }
        public string DisplayName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public Address? Billing { get; set; }
        public Address? Shipping { get; set; }
        public List<string> Tags { get; set; } = new List<string>();
    }

    public sealed class Address
    {
        public string Line1 { get; set; } = string.Empty;
        public string Line2 { get; set; } = string.Empty;
        public string PostalCode { get; set; } = string.Empty;
        public string City { get; set; } = string.Empty;
        public string CountryCode { get; set; } = string.Empty;
    }

    public sealed class OrderLine
    {
        public int LineNumber { get; set; }
        public string Sku { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public int Quantity { get; set; }
        public decimal UnitPrice { get; set; }
    }

    /// <summary>
    /// Element of a large homogeneous collection - the shape where per-item overhead dominates.
    /// </summary>
    public sealed class LogEntry
    {
        public long Sequence { get; set; }
        public DateTime TimestampUtc { get; set; }
        public int Level { get; set; }
        public string Source { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public bool Succeeded { get; set; }
        public double DurationMs { get; set; }
    }

    /// <summary>
    /// GUID-dense record - GUIDs are 16 raw bytes binary but 36 characters as text, so this is
    /// the shape where a binary format should win hardest against a text one.
    /// </summary>
    public sealed class CorrelationRecord
    {
        public Guid Id { get; set; }
        public Guid TraceId { get; set; }
        public Guid SpanId { get; set; }
        public Guid ParentSpanId { get; set; }
        public Guid TenantId { get; set; }
        public Guid SessionId { get; set; }
        public Guid RequestId { get; set; }
        public Guid CorrelationId { get; set; }
        public int Sequence { get; set; }
    }

    /*
     * Polymorphic hierarchy. fastBinaryJSON handles this natively by embedding the full
     * AssemblyQualifiedName in a $type field. System.Text.Json needs the derived types
     * declared, hence the attributes - without them it would serialize only the base
     * surface and the comparison would be dishonest.
     */

    [JsonDerivedType(typeof(Circle), nameof(Circle))]
    [JsonDerivedType(typeof(Rectangle), nameof(Rectangle))]
    [JsonDerivedType(typeof(Triangle), nameof(Triangle))]
    public abstract class Shape
    {
        public int Id { get; set; }
        public string Label { get; set; } = string.Empty;
    }

    public sealed class Circle : Shape
    {
        public double Radius { get; set; }
    }

    public sealed class Rectangle : Shape
    {
        public double Width { get; set; }
        public double Height { get; set; }
    }

    public sealed class Triangle : Shape
    {
        public double BaseLength { get; set; }
        public double Height { get; set; }
        public double Skew { get; set; }
    }

    /// <summary>
    /// Wrapper so the polymorphic payload is a single root object rather than a bare list.
    /// </summary>
    public sealed class ShapeCatalogue
    {
        public string Name { get; set; } = string.Empty;
        public List<Shape> Shapes { get; set; } = new List<Shape>();
    }

    /// <summary>
    /// Isolates a defect, not a shape. fastBinaryJSON writes `char` as a short (WriteChar) but
    /// ParseChar returns that short unconverted, so a typed round trip throws InvalidCastException
    /// and an untyped BJSON.Parse silently yields a boxed short instead of a char.
    ///
    /// Present since the commit that introduced char support (v1.4.11) and unchanged in every
    /// release after it. Kept in the corpus so the compatibility matrix records it as a standing
    /// FAIL, and so it becomes the red test when the fix lands.
    /// </summary>
    public sealed class CharHolder
    {
        public char Value { get; set; }
        public string Context { get; set; } = string.Empty;
    }
}
