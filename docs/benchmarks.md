# Benchmark: this fork against upstream fastBinaryJSON 1.6.1

Measured 2026-10-06 with BenchmarkDotNet 0.15.8 (3 launches, 3 warmups, 5 iterations each) on an
Apple M5 Pro, .NET 10.0.12, Arm64, net10.0 asset of this package at commit `b70d29b`. Upstream is the
1.6.1 package from NuGet. Both write the same bytes for every payload below, so the comparison is
like for like. The corpus and the benchmark classes are in `benchmarks/FastBinaryJson.Benchmarks`;
run them with `dotnet run -c Release --project benchmarks/FastBinaryJson.Benchmarks -- bench --filter '*'`.

Ratios are fork over upstream, so lower is better. The "(run 1)" columns are the same ratios from an
earlier run the same day, before four optimizations: the serializer's id and type tables are kept per
thread between calls, a UTF-16 `$type` is resolved without a zeroed stack buffer, ordinary objects skip
the chain of special cases in the writer, and the parameter copy shares the ignore list.

```
op          payload          enc    upstream ns    fork ns    time  (run 1)  upstream B    fork B   alloc  (run 1)
Serialize   FlatPrimitives   utf16          604        211   0.35x    0.43x       5,576     1,016   0.18x    0.28x
Serialize   FlatPrimitives   utf8           617        219   0.35x    0.42x       3,880       688   0.18x    0.31x
Serialize   NestedOrder      utf16        8,047      2,490   0.31x    0.34x      68,152     8,440   0.12x    0.16x
Serialize   NestedOrder      utf8         8,589      2,850   0.33x    0.35x      45,792     5,344   0.12x    0.17x
Serialize   LargeCollection  utf16      708,874    116,682   0.16x    0.18x   3,928,622   583,914   0.15x    0.17x
Serialize   LargeCollection  utf8       423,414    129,468   0.31x    0.33x   2,379,564   333,239   0.14x    0.18x
Serialize   GuidDense        utf16       87,640     27,576   0.31x    0.31x     683,774   122,226   0.18x    0.21x
Serialize   GuidDense        utf8        75,550     20,108   0.27x    0.29x     599,948    80,000   0.13x    0.17x
Deserialize FlatPrimitives   utf16          739        311   0.42x    0.48x       4,688     1,656   0.35x    0.40x
Deserialize FlatPrimitives   utf8           761        341   0.45x    0.52x       4,688     1,656   0.35x    0.40x
Deserialize NestedOrder      utf16        9,299      3,307   0.36x    0.42x      42,240    10,048   0.24x    0.24x
Deserialize NestedOrder      utf8         9,992      4,101   0.41x    0.41x      42,240    10,048   0.24x    0.24x
Deserialize LargeCollection  utf16      463,980    111,802   0.24x    0.25x   1,913,112   285,152   0.15x    0.15x
Deserialize LargeCollection  utf8       501,168    122,783   0.24x    0.24x   1,913,112   285,152   0.15x    0.15x
Deserialize GuidDense        utf16       92,273     19,691   0.21x    0.21x     500,544    40,856   0.08x    0.08x
Deserialize GuidDense        utf8        97,941     21,256   0.22x    0.20x     500,544    40,856   0.08x    0.08x
```

## Summary

- Time: 0.16x to 0.45x of upstream, roughly 2 to 6 times faster, in both encodings and both directions.
- Allocations: 0.08x to 0.35x of upstream. Deserializing the GuidDense payload allocates 41 KB where
  upstream allocates 500 KB.
- Against run 1, serialize allocations fell another 20 to 40 percent on small payloads and serialize time
  by up to 20 percent (FlatPrimitives); deserialize FlatPrimitives is about 10 percent faster. Large
  payloads moved little, as expected: their cost is the output and the strings.

## Caveats

- net10.0 only. The netstandard2.0 asset has no span-based paths and measures 8 to 11 percent slower
  than the pre-fork unsafe version on UTF-16 serialize (run on the net10 runtime).
- LargeCollection deserialize UTF-16 is noisy (error 22 microseconds on a 117 microsecond mean).
- Raw payload size is identical to upstream except where upstream writes wrong bytes: a dictionary key
  over 256 encoded bytes is 838 bytes here against 323 upstream, because upstream truncates the length.
