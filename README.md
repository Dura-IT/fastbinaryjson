# DuraIT.FastBinaryJson

Binary JSON serializer for .NET with attribute-free runtime polymorphism. A modernized,
maintained fork of [mgholam/fastBinaryJSON](https://github.com/mgholam/fastBinaryJSON).

## Why this fork exists

fastBinaryJSON does one thing that is genuinely hard to replace: runtime polymorphism with no
attributes, no schema and no generated code. An interface- or base-typed property comes back as the
concrete type it was written as, with nothing declared up front.

This fork keeps that on current .NET and builds real engineering around it - a test suite that runs
on every push, byte-level proof that the wire format has not moved, and a maintained release path.
Data written by the original keeps reading back, and that is enforced by tests rather than promised.

## What we add

- **Proof the wire format has not moved.** 16 committed golden fixtures assert the exact bytes
  produced, not just that a round-trip succeeds - so a corruption that reproduces itself
  symmetrically still fails. Over 400 tests in total.
- **Verified on Linux, macOS and Windows, in both build configurations, on every push.** A wire-format
  bug that shows up on one platform only is precisely the class this catches.
- **Debug and Release produce identical output.** Both configurations are built and tested in CI, so
  they cannot quietly drift apart.
- **One SDK-style project**, centralised build output, current SDK tooling.
- **Defects found and fixed, each with a regression test.** The first five are listed below; more
  turned up while fixing them and are fixed the same way.
- **Clean under strict static analysis.** Sonar, Roslynator and the full .NET analyzer set run with
  warnings as errors, and the build is at zero warnings.

## Compatibility

The wire format is unchanged, and the golden fixtures are what keep it that way. Bytes written by
the original read back through this package, and bytes written here read back through the original.
That includes DataSet and DataTable payloads: the embedded schema object is written under upstream's
type name and resolved back to this package's type when read, and a test reads payloads written by
upstream fastBinaryJSON 1.6.1 itself. The exception is a payload written with
`UseOptimizedDatasetSchema` off, which is XML and not covered by a fixture.

**Source compatibility costs a rename.** The namespace is `DuraIT.FastBinaryJson`, matching the
assembly and package name; upstream's was `fastBinaryJSON`. The public API also follows .NET naming
now, so a migration is a `using` swap plus the renames in the table below. The compiler finds every
one of them; none changes what a call does.

**Stored data is not affected by that rename.** The `$type` entries in a payload carry the assembly
qualified names of *your* types, never the serializer's, so renaming this library's namespace cannot
invalidate anything already on disk. The one library type that does appear in a payload is the
DataSet/DataTable schema object, which keeps upstream's name on the wire (see above). None of the 16
golden fixtures reference this assembly; no golden fixture covers a DataSet.

Assembly name and namespace both differ from the original, so this package and `fastBinaryJSON` can
sit in the same project with no duplicate type definitions and no ambiguity.

`netstandard2.0` is a permanent target, not a leftover: the people with stored data are exactly the
ones who cannot move runtime quickly. On that target the package references `System.Memory`, which is part
of the runtime on .NET Core and a regular NuGet package on .NET Framework. An application or test
project on .NET Framework gets the assembly binding redirects generated for it; a class library, or a
plug-in loaded into a host that owns the `app.config`, does not, and needs the redirects added by the
host.

### Migrating from fastBinaryJSON

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

Two behaviours differ on purpose. An unrecognized token or a graph deeper than `SerializerMaxDepth`
throws `BjsonException`, where upstream threw a bare `Exception`; it still derives from `Exception`,
so a handler written for the base type keeps working. Other corrupt or truncated input can still
surface as `IndexOutOfRangeException`, `ArgumentOutOfRangeException` or `NullReferenceException`; do
not rely on catching only `BjsonException` for untrusted bytes. And a null argument to
a public method throws `ArgumentNullException` up front. The one exception is `DeepCopy(null)`, which
returns null, since null is a valid thing to serialize.

`Reflection.RDBMode`, which let RaptorDB-style consumers resolve runtime-loaded assemblies, is gone: it
went away with the type, and its code path was removed rather than left unreachable.

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

## The five inherited defects

All five are present in the original and all five are fixed here, each in its own commit, with
round-trip tests in
[`tests/FastBinaryJson.UnitTests/RoundTrip/`](https://github.com/Dura-IT/fastbinaryjson/tree/master/tests/FastBinaryJson.UnitTests/RoundTrip)
that replaced the characterization tests which used to pin the broken behaviour in place.

The table describes what the original does.

| Defect | Effect |
|---|---|
| `char` does not round-trip | Typed deserialize throws; untyped returns a boxed `Int16` |
| `sbyte` does not round-trip | Indistinguishable from `byte` on the wire; `-42` comes back as `214` |
| Long names truncate | At 256 encoded bytes, which is 128 characters at the default UTF-16 setting. Serves dictionary keys, so it reaches real data, and nothing throws |
| `DateTimeOffset` cannot be serialized | The token is declared but never written or read; serializing one produces invalid IL |
| Custom types skip subclasses | A registration for a base type is not used for a derived instance |

Two of the fixes change the bytes this version writes: `sbyte` gets its own token instead of being
written as `byte`, and a name of 256 encoded bytes or more is no longer truncated. Data written by
the original still reads back in both cases; data written by this version does not read back through
the original. The `char` fix is read-side only, so its output is byte-identical to before, and
`DateTimeOffset` uses a token the original declared but never wrote, so no existing payload can
contain one.

A sixth defect was found while fixing these: `UseUtcDateTime` converted on write and again on read,
so a value came back shifted by the reading machine's time zone. The bytes were always correct. That
fix is read-side only as well.

## License

MIT, both the original work and the changes in this fork. See
[LICENSE](https://github.com/Dura-IT/fastbinaryjson/blob/master/LICENSE).

Copyright (c) 2010-2019 Mehdi Gholam for the original library; the fork is by Ben de Bruijn /
Durable IT Solutions. Upstream's own changelog is kept verbatim at
[docs/upstream-history.txt](https://github.com/Dura-IT/fastbinaryjson/blob/master/docs/upstream-history.txt).
