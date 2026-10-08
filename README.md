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
