# Benchmark: this package against upstream fastBinaryJSON 1.6.1 and System.Text.Json

Measured 2026-10-10 with BenchmarkDotNet 0.15.8 (3 launches, 3 warmups, 5 iterations each) on an Apple M5 Pro,
.NET 10.0.12, Arm64, the net10.0 build at the head of pull request 17 (`ee44478`, 0.3.0 plus per-call parameter
overloads), on an otherwise idle machine kept awake for the whole run. Upstream is the 1.6.1 package from NuGet and
System.Text.Json uses its default options (`JsonSerializerDefaults.General`). This package and upstream write the same
bytes for every payload below. The corpus and the benchmark classes are in `benchmarks/FastBinaryJson.Benchmarks`.

Reproduce:

```
dotnet run -c Release -f net10.0 --project benchmarks/FastBinaryJson.Benchmarks -- bench --filter '*ThroughputBenchmarks*'
dotnet run -c Release -f net10.0 --project benchmarks/FastBinaryJson.Benchmarks -- sizes
```

Arms: `fbj-utf16` is this package at its defaults and `fbj-utf8` the same with `UseUnicodeStrings = false`;
`upstream-utf16` and `upstream-utf8` are the original with the same two settings; `stj` is System.Text.Json and
`msgpack` is MessagePack-CSharp. MessagePack's GuidDense deserialize (193 us) is far slower than its other cases and
was not investigated.

An earlier run of the UTF-16 arms on 0.3.0 (2026-10-08) differs from this one by up to 3% on every case except
FlatPrimitives deserialize, which was 10% slower then (256 ns against 231 ns). The README tables still carry those
figures.

```
| Method      | Payload         | Arm            | Mean         | Error        | StdDev       | Gen0     | Gen1     | Gen2     | Allocated |
|------------ |---------------- |--------------- |-------------:|-------------:|-------------:|---------:|---------:|---------:|----------:|
| Serialize   | FlatPrimitives  | fbj-utf16      |     221.7 ns |      8.19 ns |      7.26 ns |   0.1299 |        - |        - |    1088 B |
| Deserialize | FlatPrimitives  | fbj-utf16      |     230.8 ns |     11.44 ns |     10.14 ns |   0.0947 |        - |        - |     792 B |
| Serialize   | FlatPrimitives  | fbj-utf8       |     218.4 ns |     10.28 ns |      9.11 ns |   0.0908 |        - |        - |     760 B |
| Deserialize | FlatPrimitives  | fbj-utf8       |     246.3 ns |      3.16 ns |      2.95 ns |   0.0944 |        - |        - |     792 B |
| Serialize   | FlatPrimitives  | msgpack        |     146.8 ns |      1.15 ns |      0.96 ns |   0.0401 |        - |        - |     336 B |
| Deserialize | FlatPrimitives  | msgpack        |     222.6 ns |      1.44 ns |      1.28 ns |   0.0267 |        - |        - |     224 B |
| Serialize   | FlatPrimitives  | stj            |     261.0 ns |      2.61 ns |      2.45 ns |   0.0515 |        - |        - |     432 B |
| Deserialize | FlatPrimitives  | stj            |     449.3 ns |      3.55 ns |      2.77 ns |   0.0267 |        - |        - |     224 B |
| Serialize   | FlatPrimitives  | upstream-utf16 |     591.0 ns |      2.57 ns |      2.28 ns |   0.6666 |   0.0038 |        - |    5576 B |
| Deserialize | FlatPrimitives  | upstream-utf16 |     727.1 ns |      3.74 ns |      3.32 ns |   0.5598 |   0.0067 |        - |    4688 B |
| Serialize   | FlatPrimitives  | upstream-utf8  |     605.2 ns |      1.40 ns |      1.31 ns |   0.4635 |   0.0019 |        - |    3880 B |
| Deserialize | FlatPrimitives  | upstream-utf8  |     747.2 ns |      1.31 ns |      1.02 ns |   0.5598 |   0.0067 |        - |    4688 B |
| Serialize   | GuidDense       | fbj-utf16      |  24,184.1 ns |    205.74 ns |    192.45 ns |  38.4521 |  38.4521 |  38.4521 |  122298 B |
| Deserialize | GuidDense       | fbj-utf16      |  20,146.8 ns |    946.66 ns |    839.19 ns |   4.8828 |   0.5798 |        - |   40928 B |
| Serialize   | GuidDense       | fbj-utf8       |  19,893.6 ns |    405.59 ns |    359.55 ns |   9.5215 |        - |        - |   80072 B |
| Deserialize | GuidDense       | fbj-utf8       |  19,362.4 ns |    442.73 ns |    392.47 ns |   4.8828 |   0.5798 |        - |   40928 B |
| Serialize   | GuidDense       | msgpack        |  27,629.2 ns |    155.48 ns |    137.83 ns |   9.2468 |        - |        - |   77904 B |
| Deserialize | GuidDense       | msgpack        | 193,294.1 ns |  4,052.28 ns |  3,592.24 ns |   3.6621 |   0.2441 |        - |   32056 B |
| Serialize   | GuidDense       | stj            |  18,540.5 ns |     84.70 ns |     79.22 ns |   9.9792 |        - |        - |   84032 B |
| Deserialize | GuidDense       | stj            |  58,756.8 ns |    258.59 ns |    215.93 ns |   4.1504 |   0.4272 |        - |   35136 B |
| Serialize   | GuidDense       | upstream-utf16 |  88,083.5 ns |    344.32 ns |    305.23 ns |  79.9561 |  79.9561 |  79.9561 |  683774 B |
| Deserialize | GuidDense       | upstream-utf16 |  92,866.6 ns |    943.04 ns |    835.98 ns |  59.8145 |   0.4883 |        - |  500544 B |
| Serialize   | GuidDense       | upstream-utf8  |  75,936.0 ns |  1,087.04 ns |  1,016.82 ns |  83.2520 |  41.6260 |  41.6260 |  599948 B |
| Deserialize | GuidDense       | upstream-utf8  |  96,294.3 ns |    349.48 ns |    309.80 ns |  59.8145 |   0.4883 |        - |  500544 B |
| Serialize   | LargeCollection | fbj-utf16      | 118,901.9 ns |    543.86 ns |    508.72 ns | 123.7793 | 123.5352 | 123.5352 |  583948 B |
| Deserialize | LargeCollection | fbj-utf16      | 104,436.9 ns |  3,478.09 ns |  2,904.36 ns |  34.0576 |  11.3525 |        - |  285224 B |
| Serialize   | LargeCollection | fbj-utf8       | 131,970.5 ns |  1,203.11 ns |  1,125.39 ns |  83.2520 |  83.0078 |  83.0078 |  333294 B |
| Deserialize | LargeCollection | fbj-utf8       | 120,526.2 ns |  3,908.29 ns |  3,464.60 ns |  34.0576 |  11.3525 |        - |  285224 B |
| Serialize   | LargeCollection | msgpack        |  67,631.9 ns |  6,768.77 ns |  6,000.34 ns |  47.6074 |  47.6074 |  47.6074 |  157024 B |
| Deserialize | LargeCollection | msgpack        | 117,835.7 ns |    698.69 ns |    619.37 ns |  30.0293 |   9.7656 |        - |  251680 B |
| Serialize   | LargeCollection | stj            | 171,839.6 ns |  2,222.82 ns |  2,079.22 ns |  62.2559 |  62.2559 |  62.2559 |  208343 B |
| Deserialize | LargeCollection | stj            | 281,189.5 ns |  2,169.38 ns |  1,811.53 ns |  30.7617 |  10.2539 |        - |  260696 B |
| Serialize   | LargeCollection | upstream-utf16 | 671,229.3 ns | 79,472.42 ns | 70,450.23 ns | 644.5313 | 479.4922 | 479.4922 | 3928684 B |
| Deserialize | LargeCollection | upstream-utf16 | 466,639.8 ns |  3,668.39 ns |  3,251.94 ns | 228.5156 | 113.7695 |        - | 1913112 B |
| Serialize   | LargeCollection | upstream-utf8  | 426,504.1 ns |  1,238.33 ns |    966.81 ns | 374.5117 | 374.5117 | 374.5117 | 2379564 B |
| Deserialize | LargeCollection | upstream-utf8  | 509,012.2 ns |  5,423.94 ns |  4,529.24 ns | 228.5156 | 113.2813 |        - | 1913112 B |
| Serialize   | NestedOrder     | fbj-utf16      |   2,403.5 ns |      8.82 ns |      7.82 ns |   1.0147 |        - |        - |    8512 B |
| Deserialize | NestedOrder     | fbj-utf16      |   3,418.6 ns |    166.24 ns |    138.82 ns |   0.9842 |   0.0229 |        - |    8248 B |
| Serialize   | NestedOrder     | fbj-utf8       |   2,809.6 ns |     19.49 ns |     18.23 ns |   0.6447 |        - |        - |    5416 B |
| Deserialize | NestedOrder     | fbj-utf8       |   3,745.7 ns |     22.81 ns |     20.22 ns |   0.9842 |   0.0229 |        - |    8248 B |
| Serialize   | NestedOrder     | msgpack        |   1,585.8 ns |     10.61 ns |      9.92 ns |   0.3624 |        - |        - |    3032 B |
| Deserialize | NestedOrder     | msgpack        |   3,218.7 ns |     57.23 ns |     50.73 ns |   0.7439 |   0.0153 |        - |    6240 B |
| Serialize   | NestedOrder     | stj            |   2,441.4 ns |     14.49 ns |     12.85 ns |   0.4616 |        - |        - |    3864 B |
| Deserialize | NestedOrder     | stj            |   5,628.6 ns |    259.13 ns |    229.71 ns |   0.8392 |   0.0153 |        - |    7080 B |
| Serialize   | NestedOrder     | upstream-utf16 |   8,180.2 ns |     38.09 ns |     31.80 ns |   8.1177 |   0.4730 |        - |   68152 B |
| Deserialize | NestedOrder     | upstream-utf16 |   9,468.8 ns |     81.16 ns |     71.95 ns |   5.0354 |   0.5341 |        - |   42240 B |
| Serialize   | NestedOrder     | upstream-utf8  |   8,654.9 ns |     71.73 ns |     63.59 ns |   5.4626 |   0.2136 |        - |   45792 B |
| Deserialize | NestedOrder     | upstream-utf8  |  10,165.4 ns |     41.38 ns |     36.68 ns |   5.0354 |   0.5341 |        - |   42240 B |
```

## Notes

- The payloads are synthetic and small. Your own data decides the result, so measure it.
- System.Text.Json writes no type information, so it cannot read back a polymorphic graph without extra
  configuration. This package can, which these benchmarks do not measure.
- Timings on a shared CI runner are noisier than on an idle workstation. A .NET Framework 4.8 run is available
  through the `benchmark-net48` workflow.
- The size and gzip tables are in the README; the `sizes` command above prints them, along with the cases where this
  package's bytes differ from upstream's (for example a dictionary key of 256 encoded bytes or more).
