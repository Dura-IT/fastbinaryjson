# DuraIT.FastBinaryJson

Binary JSON serializer for .NET with attribute-free runtime polymorphism. A modernized,
maintained fork of [mgholam/fastBinaryJSON](https://github.com/mgholam/fastBinaryJSON).

```
dotnet add package DuraIT.FastBinaryJson
```

Targets .NET 10 and netstandard2.0.

## About

fastBinaryJSON does one thing that is hard to replace: runtime polymorphism with no attributes, no
schema and no generated code. An interface- or base-typed property comes back as the concrete type it
was written as, with nothing declared up front.

This package keeps that on current .NET, with a test suite that runs on every push, byte-level proof
that the wire format has not moved, and a maintained release path. Data written by the original keeps
reading back, and tests enforce it.

## Features

- **Stable wire format.** Committed golden fixtures assert the exact bytes produced, not just that a
  round-trip succeeds, so a corruption that reproduces itself symmetrically still fails.
- **Tested on Linux, macOS and Windows,** in Debug and Release, on every push.
- **Strong-name signed** with its own key (public key token `8cd8b707f36b302a`), so a strong-named
  application can reference it.
- **Predictable errors.** Bytes that are not a valid payload throw `BjsonException` from every entry
  point that reads bytes.
- **Clean under strict static analysis.** Sonar, Roslynator and the full .NET analyzer set run with
  warnings as errors.

## Usage

```csharp
using DuraIT.FastBinaryJson;

byte[] bytes = Bjson.ToBjson(myObject);
MyType back = Bjson.ToObject<MyType>(bytes);
```

Untyped, when the target type is not known at the call site:

```csharp
object graph = Bjson.Parse(bytes);
```

Settings go through `BjsonParameters`, per call or globally:

```csharp
byte[] smaller = Bjson.ToBjson(myObject, new BjsonParameters
{
    UseUnicodeStrings = false,   // UTF-8: smaller output than the default UTF-16
    UsingGlobalTypes = true      // one $types table instead of repeating type names
});
```

## Performance

Measured with the benchmark project in this repository, on the net10.0 build of 0.3.0, against
fastBinaryJSON 1.6.1 and System.Text.Json with its default options (UTF-16 strings, this package's
default). The payloads are synthetic: **FlatPrimitives** is one flat object with every primitive type,
**NestedOrder** an order with 25 lines, **LargeCollection** 1000 homogeneous items, **GuidDense** 200
records of 8 GUIDs each. Lower is better in every table. Your payloads will differ, so run the benchmark
on yours before relying on any of this.

Time per call, microseconds:

| Payload | Serialize: this package | upstream | STJ | Deserialize: this package | upstream | STJ |
|---|---:|---:|---:|---:|---:|---:|
| FlatPrimitives | 0.22 | 0.62 | 0.27 | 0.26 | 0.75 | 0.47 |
| NestedOrder | 2.44 | 8.23 | 2.46 | 3.34 | 9.56 | 5.52 |
| LargeCollection | 118 | 671 | 173 | 103 | 476 | 285 |
| GuidDense | 24.9 | 90.9 | 18.6 | 19.9 | 95.3 | 60.0 |

Allocation per call, bytes:

| Payload | Serialize: this package | upstream | STJ | Deserialize: this package | upstream | STJ |
|---|---:|---:|---:|---:|---:|---:|
| FlatPrimitives | 1,088 | 5,576 | 432 | 792 | 4,688 | 224 |
| NestedOrder | 8,512 | 68,152 | 3,864 | 8,248 | 42,240 | 7,080 |
| LargeCollection | 583,962 | 3,928,684 | 208,343 | 285,224 | 1,913,112 | 260,696 |
| GuidDense | 122,298 | 683,774 | 84,032 | 40,928 | 500,544 | 35,136 |

Size on the wire, bytes. On these four payloads this package writes the same bytes as upstream (the cases
where it does not are listed under Compatibility below), so one column covers both. The UTF-8 setting (`UseUnicodeStrings = false`) is the smaller of the two encodings:

| Payload | This package, UTF-16 | UTF-8 | STJ | Gzip: this package, UTF-16 | UTF-8 | STJ |
|---|---:|---:|---:|---:|---:|---:|
| FlatPrimitives | 838 | 507 | 408 | 468 | 413 | 276 |
| NestedOrder | 8,206 | 5,109 | 3,524 | 1,529 | 1,352 | 1,056 |
| LargeCollection | 582,675 | 332,338 | 207,942 | 41,232 | 36,263 | 34,150 |
| GuidDense | 122,001 | 79,801 | 83,691 | 30,890 | 30,039 | 37,199 |

What the numbers say: against the original this package is roughly 3 to 6 times faster and allocates a
fraction of the memory, with the same bytes on these payloads. Against System.Text.Json it is faster to deserialize on all
four payloads and level or faster to serialize on three of them, but it allocates more in every case
and writes larger output, except for GUID-heavy data, where the UTF-8 setting and gzip both come out
smaller. System.Text.Json
does not carry type information, so it cannot read back a polymorphic graph without extra configuration.
The raw BenchmarkDotNet output for the UTF-16 arms, the machine and the method are in
[docs/benchmarks.md](https://github.com/Dura-IT/fastbinaryjson/blob/master/docs/benchmarks.md). Run the
timing and allocation benchmark yourself with
`dotnet run -c Release -f net10.0 --project benchmarks/FastBinaryJson.Benchmarks -- bench --filter '*ThroughputBenchmarks*'`,
and print the size and gzip tables with
`dotnet run -c Release -f net10.0 --project benchmarks/FastBinaryJson.Benchmarks -- sizes`.

## Compatibility with fastBinaryJSON

Bytes written by the original read back through this package, including DataSet and DataTable
payloads (a test reads payloads written by fastBinaryJSON 1.6.1 itself). The exception is a payload
written with `UseOptimizedDatasetSchema` off, which is XML and not covered by a fixture.

Output matches the original except in these cases, and data written this way does not read back
through the original:

- `sbyte` has its own token instead of being written as `byte`.
- A name of 256 encoded bytes or more is written in full instead of truncated.
- `DateTimeOffset` is supported, as ticks plus the offset in minutes under token 27, which the
  original declared but never wrote.

Assembly name and namespace both differ from the original, so this package and `fastBinaryJSON` can
sit in the same project with no duplicate type definitions. The `$type` entries in a payload carry the
assembly-qualified names of your own types, never the serializer's, so the namespace change does not
affect stored data.

On netstandard2.0 the package references `System.Memory`, which is part of the runtime on .NET Core and a
regular NuGet package on .NET Framework. An application or test project on .NET Framework gets the
assembly binding redirects generated for it; a class library, or a plug-in loaded into a host that owns
the `app.config`, needs the redirects added by the host.

### Migrating from fastBinaryJSON

The public API follows .NET naming, so a migration is a `using` swap plus the renames below. The
compiler finds every one of them, and none changes what a call does.

| fastBinaryJSON | DuraIT.FastBinaryJson |
|---|---|
| `using fastBinaryJSON;` | `using DuraIT.FastBinaryJson;` |
| `BJSON` | `Bjson` |
| `BJSON.ToBJSON(...)` | `Bjson.ToBjson(...)` |
| `BJSONParameters` | `BjsonParameters` |
| `BJSONParameters.UseUTCDateTime` | `BjsonParameters.UseUtcDateTime` |
| `BJSONParameters.v1_4TypedArray` | `BjsonParameters.UseV14TypedArray` |
| `BJSONParameters.IgnoreAttributes = new List<Type> { ... }` | `IgnoreAttributes` is read-only; call `Clear()` and `Add(...)` on it |
| `BJSON.Parameters` (a field) | `Bjson.Parameters` (a property) |
| `Reflection.Serialize` / `Reflection.Deserialize`, as passed to `RegisterCustomType` | `CustomTypeSerializer` / `CustomTypeDeserializer` |
| `TOKENS.DOC_START`, `TOKENS.INT`, ... | `Tokens.DocStart`, `Tokens.Int32`, ... (spelled-out type names) |
| `TypedArray.typename` / `count` / `data` | `TypedArray.TypeName` / `Count` / `Data` |
| `Reflection`, `myPropInfo`, `Getters`, `DatasetSchema` | no longer public |

Two behaviours differ on purpose. Bytes that are not a valid payload (truncated, corrupt, a declared
size that does not fit, a type name that cannot be created) throw `BjsonException`, with the original
exception as `InnerException`; so does a graph deeper than `SerializerMaxDepth`. Upstream threw a bare
`Exception` for some of these and `IndexOutOfRangeException`, `NullReferenceException` or an
out-of-memory for the rest. `BjsonException` derives from `Exception`, so a handler written for the base
type keeps working. And a null argument to a public method throws `ArgumentNullException` up front,
except `DeepCopy(null)`, which returns null.

`Reflection.RDBMode` is gone along with the `Reflection` type.

## Security

Do not deserialize bytes from an untrusted source into a type graph you do not control, because the
format carries type names. To report a vulnerability, see
[SECURITY.md](https://github.com/Dura-IT/fastbinaryjson/blob/master/SECURITY.md).

## License

MIT, both the original work and the changes in this package. See
[LICENSE](https://github.com/Dura-IT/fastbinaryjson/blob/master/LICENSE).

Copyright (c) 2010-2019 Mehdi Gholam for the original library; copyright (c) 2026 Durable IT Solutions
for the changes. Upstream's own changelog is kept verbatim at
[docs/upstream-history.txt](https://github.com/Dura-IT/fastbinaryjson/blob/master/docs/upstream-history.txt).
