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
  symmetrically still fails. 114 tests in total.
- **Verified on Linux, macOS and Windows, in both build configurations, on every push.** A wire-format
  bug that shows up on one platform only is precisely the class this catches.
- **Debug and Release produce identical output.** Both configurations are built and tested in CI, so
  they cannot quietly drift apart.
- **One SDK-style project**, centralised build output, current SDK tooling.
- **Five defects found, pinned and documented.** Each is held in place by a characterization test, so
  fixing one has to be a deliberate act. See below.

## Compatibility

The wire format is unchanged, and the golden fixtures are what keep it that way.

The assembly name is `DuraIT.FastBinaryJson`, deliberately different from the original, so this
package can sit in the same project as `fastBinaryJSON` without duplicate type definitions.

`netstandard2.0` is a permanent target, not a leftover: the people with stored data are exactly the
ones who cannot move runtime quickly.

## Usage

```csharp
byte[] bytes = BJSON.ToBJSON(myObject);
MyType back = BJSON.ToObject<MyType>(bytes);
```

Untyped, when the target type is not known at the call site:

```csharp
object graph = BJSON.Parse(bytes);
```

Settings go through `BJSONParameters`, per call or globally:

```csharp
byte[] smaller = BJSON.ToBJSON(myObject, new BJSONParameters
{
    UseUnicodeStrings = false,   // UTF-8: smaller output than the default UTF-16
    UsingGlobalTypes = true      // one $types table instead of repeating type names
});
```

## The five inherited defects

All five are present in the original. Here they are pinned by characterization tests in
[`KnownDefectTests.cs`](https://github.com/Dura-IT/fastbinaryjson/blob/master/tests/FastBinaryJson.UnitTests/Defects/KnownDefectTests.cs),
which assert the behaviour as it currently is - wrong included - so a fix fails the build and forces
a real test to be written for it.

| Defect | Effect |
|---|---|
| `char` does not round-trip | Typed deserialize throws; untyped returns a boxed `Int16` |
| `sbyte` does not round-trip | Indistinguishable from `byte` on the wire; `-42` comes back as `214` |
| Long names truncate | At 256 encoded bytes, which is 128 characters at the default UTF-16 setting. Serves dictionary keys, so it reaches real data, and nothing throws |
| `DateTimeOffset` cannot be serialized | The token is declared but never written or read; serializing one produces invalid IL |
| Custom types skip subclasses | A registration for a base type is not used for a derived instance |

Fixing the first three changes the wire format, so on a library whose value is drop-in compatibility
that is a decision rather than a routine fix. It gets made deliberately, not folded into a patch.

## License

MIT, both the original work and the changes in this fork. See
[LICENSE](https://github.com/Dura-IT/fastbinaryjson/blob/master/LICENSE).

Copyright (c) 2010-2019 Mehdi Gholam for the original library; the fork is by Ben de Bruijn /
Durable IT Solutions. Upstream's own changelog is kept verbatim at
[docs/upstream-history.txt](https://github.com/Dura-IT/fastbinaryjson/blob/master/docs/upstream-history.txt).
