# Benchmark: this package against upstream fastBinaryJSON 1.6.1 and System.Text.Json

Measured 2026-10-12 with BenchmarkDotNet 0.15.8 (3 launches, 3 warmups, 5 iterations each) on an Apple M5 Pro,
.NET 10.0.12, Arm64, the net10.0 build of 0.4.0 plus the primitive-list writer that ships in 0.4.1 (commit `77053e7`
of the benchmark refresh branch), on an otherwise idle machine kept awake for the whole run. Upstream is the 1.6.1
package from NuGet and System.Text.Json uses its default options (`JsonSerializerDefaults.General`). This package and
upstream write the same bytes for every payload below. The corpus and the benchmark classes are in
`benchmarks/FastBinaryJson.Benchmarks`.

Reproduce:

```
dotnet run -c Release -f net10.0 --project benchmarks/FastBinaryJson.Benchmarks -- bench --filter '*ThroughputBenchmarks*'
dotnet run -c Release -f net10.0 --project benchmarks/FastBinaryJson.Benchmarks -- sizes
```

Arms: `fbj-utf16` is this package at its defaults and `fbj-utf8` the same with `UseUnicodeStrings = false`;
`upstream-utf16` and `upstream-utf8` are the original with the same two settings; `stj` is System.Text.Json and
`msgpack` is MessagePack-CSharp. MessagePack's GuidDense deserialize (193 us) is far slower than its other cases and
was not investigated.

Two cells carry wide error margins in this run and are best read as a range: LargeCollection serialize (the error
column is about 10% of the mean; the large output lands on the large object heap, so GC timing dominates, and upstream
shows the same spread) and NestedOrder deserialize. The same cases measured within 1% to 5% of these means in the
previous run on 0.4.0, and this package's reader was not changed in between.

```
| Method      | Payload         | Arm            | Mean         | Error        | StdDev       | Gen0     | Gen1     | Gen2     | Allocated |
|------------ |---------------- |--------------- |-------------:|-------------:|-------------:|---------:|---------:|---------:|----------:|
| Serialize   | FlatPrimitives  | fbj-utf16      |     222.3 ns |      6.34 ns |      5.93 ns |   0.1299 |        - |        - |    1088 B |
| Deserialize | FlatPrimitives  | fbj-utf16      |     232.9 ns |      2.96 ns |      2.62 ns |   0.0947 |        - |        - |     792 B |
| Serialize   | FlatPrimitives  | fbj-utf8       |     220.6 ns |      5.59 ns |      4.96 ns |   0.0908 |        - |        - |     760 B |
| Deserialize | FlatPrimitives  | fbj-utf8       |     260.3 ns |      4.57 ns |      3.82 ns |   0.0944 |        - |        - |     792 B |
| Serialize   | FlatPrimitives  | msgpack        |     153.7 ns |      1.58 ns |      1.48 ns |   0.0401 |        - |        - |     336 B |
| Deserialize | FlatPrimitives  | msgpack        |     234.6 ns |      8.70 ns |      7.71 ns |   0.0267 |        - |        - |     224 B |
| Serialize   | FlatPrimitives  | stj            |     272.0 ns |      4.17 ns |      3.91 ns |   0.0515 |        - |        - |     432 B |
| Deserialize | FlatPrimitives  | stj            |     469.2 ns |      6.59 ns |      5.84 ns |   0.0267 |        - |        - |     224 B |
| Serialize   | FlatPrimitives  | upstream-utf16 |     605.3 ns |      4.53 ns |      4.01 ns |   0.6666 |   0.0038 |        - |    5576 B |
| Deserialize | FlatPrimitives  | upstream-utf16 |     745.5 ns |      5.10 ns |      4.77 ns |   0.5598 |   0.0067 |        - |    4688 B |
| Serialize   | FlatPrimitives  | upstream-utf8  |     618.3 ns |      6.43 ns |      5.37 ns |   0.4635 |   0.0019 |        - |    3880 B |
| Deserialize | FlatPrimitives  | upstream-utf8  |     758.2 ns |      3.68 ns |      3.27 ns |   0.5598 |   0.0067 |        - |    4688 B |
| Serialize   | GuidDense       | fbj-utf16      |  25,236.9 ns |    345.86 ns |    306.59 ns |  38.4521 |  38.4521 |  38.4521 |  122298 B |
| Deserialize | GuidDense       | fbj-utf16      |  20,135.0 ns |    482.46 ns |    376.68 ns |   4.8828 |   0.5798 |        - |   40928 B |
| Serialize   | GuidDense       | fbj-utf8       |  20,812.2 ns |  1,799.13 ns |  1,594.88 ns |   9.5215 |        - |        - |   80072 B |
| Deserialize | GuidDense       | fbj-utf8       |  19,311.9 ns |    125.92 ns |    105.15 ns |   4.8828 |   0.5798 |        - |   40928 B |
| Serialize   | GuidDense       | msgpack        |  29,345.9 ns |  1,366.44 ns |  1,211.31 ns |   9.2468 |        - |        - |   77904 B |
| Deserialize | GuidDense       | msgpack        | 191,261.0 ns |  3,554.54 ns |  3,151.00 ns |   3.6621 |   0.2441 |        - |   32056 B |
| Serialize   | GuidDense       | stj            |  18,704.2 ns |     91.44 ns |     85.53 ns |   9.9792 |        - |        - |   84032 B |
| Deserialize | GuidDense       | stj            |  59,372.6 ns |    172.89 ns |    144.37 ns |   4.1504 |   0.4272 |        - |   35136 B |
| Serialize   | GuidDense       | upstream-utf16 |  89,183.1 ns |  1,352.42 ns |  1,198.88 ns |  79.9561 |  79.9561 |  79.9561 |  683774 B |
| Deserialize | GuidDense       | upstream-utf16 |  94,727.3 ns |    526.00 ns |    439.24 ns |  59.8145 |   0.4883 |        - |  500544 B |
| Serialize   | GuidDense       | upstream-utf8  |  76,757.7 ns |    768.73 ns |    719.07 ns |  83.2520 |  41.6260 |  41.6260 |  599948 B |
| Deserialize | GuidDense       | upstream-utf8  |  98,836.2 ns |  1,479.99 ns |  1,311.97 ns |  59.8145 |   0.4883 |        - |  500544 B |
| Serialize   | LargeCollection | fbj-utf16      | 125,880.2 ns | 12,232.31 ns | 11,442.11 ns | 123.6572 | 123.4131 | 123.4131 |  583947 B |
| Deserialize | LargeCollection | fbj-utf16      | 101,767.6 ns |    933.83 ns |    827.82 ns |  34.0576 |  11.3525 |        - |  285224 B |
| Serialize   | LargeCollection | fbj-utf8       | 130,287.3 ns |  1,200.32 ns |  1,064.05 ns |  83.2520 |  83.0078 |  83.0078 |  333303 B |
| Deserialize | LargeCollection | fbj-utf8       | 123,476.0 ns |  2,251.84 ns |  1,996.20 ns |  33.9355 |  11.2305 |        - |  285224 B |
| Serialize   | LargeCollection | msgpack        |  65,450.6 ns |  3,633.87 ns |  3,399.13 ns |  47.6074 |  47.6074 |  47.6074 |  157024 B |
| Deserialize | LargeCollection | msgpack        | 118,089.9 ns |  1,096.83 ns |    972.31 ns |  30.0293 |   9.7656 |        - |  251680 B |
| Serialize   | LargeCollection | stj            | 172,314.5 ns |    630.87 ns |    526.80 ns |  62.2559 |  62.2559 |  62.2559 |  208343 B |
| Deserialize | LargeCollection | stj            | 283,298.8 ns |  3,437.60 ns |  3,047.34 ns |  30.7617 |  10.2539 |        - |  260696 B |
| Serialize   | LargeCollection | upstream-utf16 | 665,073.8 ns | 82,079.83 ns | 72,761.63 ns | 629.8828 | 478.5156 | 464.8438 | 3928745 B |
| Deserialize | LargeCollection | upstream-utf16 | 474,409.5 ns | 11,958.40 ns | 10,600.81 ns | 228.5156 | 113.7695 |        - | 1913112 B |
| Serialize   | LargeCollection | upstream-utf8  | 427,795.1 ns |  1,467.13 ns |  1,145.44 ns | 374.5117 | 374.5117 | 374.5117 | 2379564 B |
| Deserialize | LargeCollection | upstream-utf8  | 504,466.0 ns |  3,275.38 ns |  2,903.54 ns | 228.5156 | 113.2813 |        - | 1913112 B |
| Serialize   | NestedOrder     | fbj-utf16      |   2,409.5 ns |     25.71 ns |     21.47 ns |   1.0147 |        - |        - |    8512 B |
| Deserialize | NestedOrder     | fbj-utf16      |   3,690.1 ns |    661.23 ns |    586.16 ns |   0.9842 |   0.0229 |        - |    8248 B |
| Serialize   | NestedOrder     | fbj-utf8       |   2,789.3 ns |     24.10 ns |     18.82 ns |   0.6447 |        - |        - |    5416 B |
| Deserialize | NestedOrder     | fbj-utf8       |   3,778.2 ns |     32.56 ns |     27.19 ns |   0.9842 |   0.0229 |        - |    8248 B |
| Serialize   | NestedOrder     | msgpack        |   1,600.4 ns |     23.77 ns |     21.07 ns |   0.3624 |        - |        - |    3032 B |
| Deserialize | NestedOrder     | msgpack        |   3,201.0 ns |     47.10 ns |     44.06 ns |   0.7439 |   0.0153 |        - |    6240 B |
| Serialize   | NestedOrder     | stj            |   2,443.2 ns |      6.81 ns |      6.03 ns |   0.4616 |        - |        - |    3864 B |
| Deserialize | NestedOrder     | stj            |   5,493.9 ns |     16.36 ns |     14.50 ns |   0.8392 |   0.0153 |        - |    7080 B |
| Serialize   | NestedOrder     | upstream-utf16 |   8,087.8 ns |     73.76 ns |     61.59 ns |   8.1177 |   0.4730 |        - |   68152 B |
| Deserialize | NestedOrder     | upstream-utf16 |   9,440.4 ns |     98.64 ns |     92.26 ns |   5.0354 |   0.5341 |        - |   42240 B |
| Serialize   | NestedOrder     | upstream-utf8  |   8,768.0 ns |    116.68 ns |    109.14 ns |   5.4626 |   0.2136 |        - |   45792 B |
| Deserialize | NestedOrder     | upstream-utf8  |  10,144.9 ns |     83.18 ns |     77.81 ns |   5.0354 |   0.5341 |        - |   42240 B |
```

## Notes

- The payloads are synthetic and small. Your own data decides the result, so measure it.
- System.Text.Json writes no type information, so it cannot read back a polymorphic graph without extra
  configuration. This package can, which these benchmarks do not measure.
- Timings on a shared CI runner are noisier than on an idle workstation. A .NET Framework 4.8 run is available
  through the `benchmark-net48` workflow.
- The size tables are in the README; the `sizes` command above prints them, along with the cases where this
  package's bytes differ from upstream's (for example a dictionary key of 256 encoded bytes or more).
