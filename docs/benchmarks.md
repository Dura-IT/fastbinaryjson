# Benchmark: this fork against upstream fastBinaryJSON 1.6.1

Measured 2026-10-06 with BenchmarkDotNet 0.15.8 (3 launches, 3 warmups, 5 iterations each) on an
Apple M5 Pro, .NET 10.0.12, Arm64, net10.0 asset of this package at commit `8c05326`. Upstream is the
1.6.1 package from NuGet. Both write the same bytes for every payload below, so the comparison is
like for like. The corpus and the benchmark classes are in `benchmarks/FastBinaryJson.Benchmarks`;
run them with `dotnet run -c Release --project benchmarks/FastBinaryJson.Benchmarks -- bench --filter '*'`.

Ratios are fork over upstream, so lower is better.

```
op          payload          enc    upstream ns    fork ns     time  upstream B    fork B    alloc
Serialize   FlatPrimitives   utf16          605        259    0.43x       5,576     1,544    0.28x
Serialize   FlatPrimitives   utf8           614        259    0.42x       3,880     1,216    0.31x
Serialize   NestedOrder      utf16        8,164      2,739    0.34x      68,152    11,080    0.16x
Serialize   NestedOrder      utf8         8,776      3,083    0.35x      45,792     7,984    0.17x
Serialize   LargeCollection  utf16      724,012    128,346    0.18x   3,928,584   685,517    0.17x
Serialize   LargeCollection  utf8       427,484    141,072    0.33x   2,379,564   435,089    0.18x
Serialize   GuidDense        utf16       88,497     27,426    0.31x     683,774   144,714    0.21x
Serialize   GuidDense        utf8        75,857     21,912    0.29x     599,948   102,488    0.17x
Deserialize FlatPrimitives   utf16          755        364    0.48x       4,688     1,872    0.40x
Deserialize FlatPrimitives   utf8           756        389    0.52x       4,688     1,872    0.40x
Deserialize NestedOrder      utf16        9,460      3,950    0.42x      42,240    10,264    0.24x
Deserialize NestedOrder      utf8        10,096      4,155    0.41x      42,240    10,264    0.24x
Deserialize LargeCollection  utf16      465,342    116,760    0.25x   1,913,112   285,368    0.15x
Deserialize LargeCollection  utf8       505,590    121,190    0.24x   1,913,112   285,368    0.15x
Deserialize GuidDense        utf16       92,648     19,617    0.21x     500,544    41,072    0.08x
Deserialize GuidDense        utf8        96,687     19,222    0.20x     500,544    41,072    0.08x
```

## Summary

- Time: 0.18x to 0.52x of upstream, roughly 2 to 5 times faster, in both encodings and both directions.
- Allocations: 0.08x to 0.40x of upstream. Deserializing the GuidDense payload allocates 41 KB where
  upstream allocates 500 KB.
- Against System.Text.Json and MessagePack (same run): the fork deserializes faster than System.Text.Json
  on every payload and faster than MessagePack on GuidDense; MessagePack still serializes faster on
  FlatPrimitives, NestedOrder and LargeCollection, and System.Text.Json serializes NestedOrder and
  GuidDense faster.

## Caveats

- net10.0 only. The netstandard2.0 asset has no span-based paths and measures 8 to 11 percent slower
  than the pre-fork unsafe version on UTF-16 serialize (run on the net10 runtime).
- LargeCollection deserialize UTF-16 is noisy (error 22 microseconds on a 117 microsecond mean).
- Raw payload size is identical to upstream except where upstream writes wrong bytes: a dictionary key
  over 256 encoded bytes is 838 bytes here against 323 upstream, because upstream truncates the length.
