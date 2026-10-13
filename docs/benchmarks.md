# Benchmark: this package against upstream fastBinaryJSON 1.6.1 and System.Text.Json

Measured 2026-10-13 with BenchmarkDotNet 0.15.8 (3 launches, 3 warmups, 5 iterations each) on an Apple M5 Pro,
.NET 10.0.12, Arm64, the net10.0 build of 0.4.1 (`fcf3bcb`) plus the primitive-list reader, on an otherwise idle machine
kept awake for the whole run. Upstream is the 1.6.1 package from NuGet and System.Text.Json uses its default options (`JsonSerializerDefaults.General`). This package and
upstream write the same bytes for every payload below. The corpus and the benchmark classes are in
`benchmarks/FastBinaryJson.Benchmarks`.

Reproduce:

```
dotnet run -c Release -f net10.0 --project benchmarks/FastBinaryJson.Benchmarks -- bench --filter '*ThroughputBenchmarks*'
dotnet run -c Release -f net10.0 --project benchmarks/FastBinaryJson.Benchmarks -- sizes
```

Arms: `fbj-utf16` is this package at its defaults and `fbj-utf8` the same with `UseUnicodeStrings = false`;
`upstream-utf16` and `upstream-utf8` are the original with the same two settings; `stj` is System.Text.Json and
`msgpack` is MessagePack-CSharp. MessagePack's GuidDense deserialize (190 us) is far slower than its other cases: MessagePack-CSharp 3.1.11
writes a Guid as a 36-character string, and reading 1,600 of them took about 233 us in isolation against 17 us for
`Guid.Parse`, so the cost is in that arm's Guid handling, not in this benchmark's harness.

Two cells carry wide error margins in this run and are best read as a range: this package's FlatPrimitives
serialize and upstream's LargeCollection serialize (the error column is about 22% of the mean for both). An earlier
full run on the same code, taken while other programs were running, was 4% to 21% slower on every arm including the
unchanged controls, and was discarded for that reason. The benchmark corpus has no `List<primitive>` member, so it
does not measure the primitive-list reader; allocation is identical to the run before it was added.

```
| Method      | Payload         | Arm            | Mean         | Error         | StdDev        | Gen0     | Gen1     | Gen2     | Allocated |
|------------ |---------------- |--------------- |-------------:|--------------:|--------------:|---------:|---------:|---------:|----------:|
| Serialize   | FlatPrimitives  | fbj-utf16      |     256.0 ns |      57.03 ns |      53.34 ns |   0.1299 |        - |        - |    1088 B |
| Deserialize | FlatPrimitives  | fbj-utf16      |     231.3 ns |       6.82 ns |       6.04 ns |   0.0947 |        - |        - |     792 B |
| Serialize   | FlatPrimitives  | fbj-utf8       |     222.3 ns |       1.89 ns |       1.58 ns |   0.0908 |        - |        - |     760 B |
| Deserialize | FlatPrimitives  | fbj-utf8       |     255.1 ns |       1.96 ns |       1.64 ns |   0.0944 |        - |        - |     792 B |
| Serialize   | FlatPrimitives  | msgpack        |     152.3 ns |       2.02 ns |       1.79 ns |   0.0401 |        - |        - |     336 B |
| Deserialize | FlatPrimitives  | msgpack        |     229.2 ns |       5.88 ns |       5.50 ns |   0.0267 |        - |        - |     224 B |
| Serialize   | FlatPrimitives  | stj            |     266.6 ns |       1.10 ns |       1.03 ns |   0.0515 |        - |        - |     432 B |
| Deserialize | FlatPrimitives  | stj            |     463.3 ns |       3.31 ns |       3.09 ns |   0.0267 |        - |        - |     224 B |
| Serialize   | FlatPrimitives  | upstream-utf16 |     606.6 ns |       1.77 ns |       1.57 ns |   0.6666 |   0.0038 |        - |    5576 B |
| Deserialize | FlatPrimitives  | upstream-utf16 |     739.3 ns |       2.59 ns |       2.30 ns |   0.5598 |   0.0067 |        - |    4688 B |
| Serialize   | FlatPrimitives  | upstream-utf8  |     618.2 ns |       4.59 ns |       4.29 ns |   0.4635 |   0.0019 |        - |    3880 B |
| Deserialize | FlatPrimitives  | upstream-utf8  |     773.0 ns |      13.13 ns |      10.25 ns |   0.5598 |   0.0067 |        - |    4688 B |
| Serialize   | GuidDense       | fbj-utf16      |  25,012.6 ns |     664.75 ns |     555.10 ns |  38.4521 |  38.4521 |  38.4521 |  122298 B |
| Deserialize | GuidDense       | fbj-utf16      |  20,509.4 ns |     517.32 ns |     458.59 ns |   4.8828 |   0.5798 |        - |   40928 B |
| Serialize   | GuidDense       | fbj-utf8       |  21,998.3 ns |   2,104.80 ns |   1,865.85 ns |   9.5215 |        - |        - |   80072 B |
| Deserialize | GuidDense       | fbj-utf8       |  20,149.3 ns |     792.80 ns |     702.79 ns |   4.8828 |   0.5798 |        - |   40928 B |
| Serialize   | GuidDense       | msgpack        |  28,949.6 ns |   1,913.00 ns |   1,597.44 ns |   9.2468 |        - |        - |   77904 B |
| Deserialize | GuidDense       | msgpack        | 190,454.7 ns |   2,436.01 ns |   2,159.46 ns |   3.6621 |   0.2441 |        - |   32056 B |
| Serialize   | GuidDense       | stj            |  18,711.7 ns |     255.04 ns |     238.56 ns |   9.9792 |        - |        - |   84032 B |
| Deserialize | GuidDense       | stj            |  59,655.6 ns |     255.50 ns |     238.99 ns |   4.1504 |   0.4272 |        - |   35136 B |
| Serialize   | GuidDense       | upstream-utf16 |  88,438.5 ns |     293.87 ns |     245.40 ns |  79.9561 |  79.9561 |  79.9561 |  683774 B |
| Deserialize | GuidDense       | upstream-utf16 |  93,294.5 ns |     757.66 ns |     671.64 ns |  59.8145 |   0.4883 |        - |  500544 B |
| Serialize   | GuidDense       | upstream-utf8  |  75,971.7 ns |     628.39 ns |     524.74 ns |  83.2520 |  41.6260 |  41.6260 |  599948 B |
| Deserialize | GuidDense       | upstream-utf8  |  97,745.7 ns |   1,335.58 ns |   1,183.96 ns |  59.8145 |   0.4883 |        - |  500544 B |
| Serialize   | LargeCollection | fbj-utf16      | 117,957.2 ns |     440.14 ns |     411.70 ns | 123.0469 | 122.8027 | 122.8027 |  583927 B |
| Deserialize | LargeCollection | fbj-utf16      | 101,248.8 ns |     127.04 ns |     106.08 ns |  34.0576 |  11.3525 |        - |  285224 B |
| Serialize   | LargeCollection | fbj-utf8       | 130,250.8 ns |     922.67 ns |     817.92 ns |  83.0078 |  83.0078 |  83.0078 |  333289 B |
| Deserialize | LargeCollection | fbj-utf8       | 120,970.9 ns |   3,217.93 ns |   2,852.61 ns |  34.0576 |  11.3525 |        - |  285224 B |
| Serialize   | LargeCollection | msgpack        |  63,310.8 ns |     925.83 ns |     773.11 ns |  47.6074 |  47.6074 |  47.6074 |  157024 B |
| Deserialize | LargeCollection | msgpack        | 117,556.1 ns |     558.81 ns |     495.37 ns |  30.0293 |   9.7656 |        - |  251680 B |
| Serialize   | LargeCollection | stj            | 170,726.4 ns |     970.84 ns |     860.63 ns |  62.2559 |  62.2559 |  62.2559 |  208343 B |
| Deserialize | LargeCollection | stj            | 280,111.2 ns |   1,079.26 ns |     956.73 ns |  30.7617 |  10.2539 |        - |  260696 B |
| Serialize   | LargeCollection | upstream-utf16 | 646,740.6 ns | 144,749.47 ns | 113,010.86 ns | 649.4141 | 498.0469 | 484.3750 | 3928737 B |
| Deserialize | LargeCollection | upstream-utf16 | 466,188.0 ns |   3,082.72 ns |   2,574.21 ns | 228.5156 | 113.7695 |        - | 1913112 B |
| Serialize   | LargeCollection | upstream-utf8  | 425,710.8 ns |   1,359.20 ns |   1,135.00 ns | 374.5117 | 374.5117 | 374.5117 | 2379564 B |
| Deserialize | LargeCollection | upstream-utf8  | 504,157.5 ns |   2,538.01 ns |   2,119.35 ns | 228.5156 | 113.2813 |        - | 1913112 B |
| Serialize   | NestedOrder     | fbj-utf16      |   2,410.2 ns |       6.20 ns |       5.50 ns |   1.0147 |        - |        - |    8512 B |
| Deserialize | NestedOrder     | fbj-utf16      |   3,086.4 ns |      16.52 ns |      14.64 ns |   0.9842 |   0.0229 |        - |    8248 B |
| Serialize   | NestedOrder     | fbj-utf8       |   2,828.1 ns |      25.40 ns |      23.75 ns |   0.6447 |        - |        - |    5416 B |
| Deserialize | NestedOrder     | fbj-utf8       |   3,679.6 ns |       8.81 ns |       7.36 ns |   0.9842 |   0.0229 |        - |    8248 B |
| Serialize   | NestedOrder     | msgpack        |   1,600.2 ns |      18.81 ns |      15.70 ns |   0.3624 |        - |        - |    3032 B |
| Deserialize | NestedOrder     | msgpack        |   3,181.6 ns |      13.22 ns |      11.72 ns |   0.7439 |   0.0153 |        - |    6240 B |
| Serialize   | NestedOrder     | stj            |   2,434.2 ns |      24.96 ns |      23.35 ns |   0.4616 |        - |        - |    3864 B |
| Deserialize | NestedOrder     | stj            |   5,391.5 ns |      90.83 ns |      75.85 ns |   0.8392 |   0.0153 |        - |    7080 B |
| Serialize   | NestedOrder     | upstream-utf16 |   8,134.9 ns |      41.79 ns |      34.90 ns |   8.1177 |   0.4730 |        - |   68152 B |
| Deserialize | NestedOrder     | upstream-utf16 |   9,329.7 ns |      35.58 ns |      29.71 ns |   5.0354 |   0.5341 |        - |   42240 B |
| Serialize   | NestedOrder     | upstream-utf8  |   8,626.7 ns |      96.43 ns |      80.52 ns |   5.4626 |   0.2136 |        - |   45792 B |
| Deserialize | NestedOrder     | upstream-utf8  |  10,150.8 ns |      97.52 ns |      81.44 ns |   5.0354 |   0.5341 |        - |   42240 B |
```

## Notes

- The payloads are synthetic and small. Your own data decides the result, so measure it.
- System.Text.Json writes no type information, so it cannot read back a polymorphic graph without extra
  configuration. This package can, which these benchmarks do not measure.
- Timings on a shared CI runner are noisier than on an idle workstation. A .NET Framework 4.8 run is available
  through the `benchmark-net48` workflow.
- The size tables are in the README; the `sizes` command above prints them, along with the cases where this
  package's bytes differ from upstream's (for example a dictionary key of 256 encoded bytes or more).
