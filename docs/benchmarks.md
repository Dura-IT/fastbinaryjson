# Benchmark: this package against upstream fastBinaryJSON 1.6.1 and System.Text.Json

Measured 2026-10-08 with BenchmarkDotNet 0.15.8 (3 launches, 3 warmups, 5 iterations each) on an Apple M5 Pro,
.NET 10.0.12, Arm64, the net10.0 build of 0.3.0 (commit `2c1e3bf`), on an otherwise idle machine. Upstream is
the 1.6.1 package from NuGet and System.Text.Json uses its default options (`JsonSerializerDefaults.General`).
This package and upstream write the same bytes for every payload below. The corpus and the benchmark classes are
in `benchmarks/FastBinaryJson.Benchmarks`.

Reproduce:

```
dotnet run -c Release -f net10.0 --project benchmarks/FastBinaryJson.Benchmarks -- bench --filter '*ThroughputBenchmarks*'
dotnet run -c Release -f net10.0 --project benchmarks/FastBinaryJson.Benchmarks -- sizes
```

The `fbj-utf16` arm is this package at its defaults; `upstream-utf16` is the original with the same setting.

```
| Method      | Payload         | Arm            | Mean         | Error        | StdDev       | Gen0     | Gen1     | Gen2     | Allocated |
|------------ |---------------- |--------------- |-------------:|-------------:|-------------:|---------:|---------:|---------:|----------:|
| Serialize   | FlatPrimitives  | fbj-utf16      |     220.5 ns |     11.41 ns |      8.91 ns |   0.1299 |        - |        - |    1088 B |
| Deserialize | FlatPrimitives  | fbj-utf16      |     256.3 ns |      6.16 ns |      5.46 ns |   0.0944 |        - |        - |     792 B |
| Serialize   | FlatPrimitives  | stj            |     267.7 ns |      3.06 ns |      2.56 ns |   0.0515 |        - |        - |     432 B |
| Deserialize | FlatPrimitives  | stj            |     467.6 ns |      1.98 ns |      1.85 ns |   0.0267 |        - |        - |     224 B |
| Serialize   | FlatPrimitives  | upstream-utf16 |     615.6 ns |      3.43 ns |      3.04 ns |   0.6666 |   0.0038 |        - |    5576 B |
| Deserialize | FlatPrimitives  | upstream-utf16 |     746.2 ns |      4.92 ns |      4.36 ns |   0.5598 |   0.0067 |        - |    4688 B |
| Serialize   | GuidDense       | fbj-utf16      |  24,916.2 ns |    362.93 ns |    321.73 ns |  38.4521 |  38.4521 |  38.4521 |  122298 B |
| Deserialize | GuidDense       | fbj-utf16      |  19,925.9 ns |    117.08 ns |    103.79 ns |   4.8828 |   0.5798 |        - |   40928 B |
| Serialize   | GuidDense       | stj            |  18,609.9 ns |     71.77 ns |     63.62 ns |   9.9792 |        - |        - |   84032 B |
| Deserialize | GuidDense       | stj            |  59,978.0 ns |    899.44 ns |    751.08 ns |   4.1504 |   0.4272 |        - |   35136 B |
| Serialize   | GuidDense       | upstream-utf16 |  90,931.2 ns |  1,321.93 ns |  1,103.88 ns |  79.9561 |  79.9561 |  79.9561 |  683774 B |
| Deserialize | GuidDense       | upstream-utf16 |  95,258.7 ns |    464.36 ns |    411.65 ns |  59.8145 |   0.4883 |        - |  500544 B |
| Serialize   | LargeCollection | fbj-utf16      | 118,329.1 ns |  1,177.86 ns |  1,044.14 ns | 123.1689 | 122.9248 | 122.9248 |  583962 B |
| Deserialize | LargeCollection | fbj-utf16      | 103,287.2 ns |    692.42 ns |    578.20 ns |  34.0576 |  11.3525 |        - |  285224 B |
| Serialize   | LargeCollection | stj            | 173,019.2 ns |  1,264.76 ns |    987.44 ns |  62.2559 |  62.2559 |  62.2559 |  208343 B |
| Deserialize | LargeCollection | stj            | 284,991.3 ns |  1,136.49 ns |    887.30 ns |  30.7617 |  10.2539 |        - |  260696 B |
| Serialize   | LargeCollection | upstream-utf16 | 671,396.5 ns | 36,131.68 ns | 32,029.80 ns | 641.6016 | 476.5625 | 476.5625 | 3928684 B |
| Deserialize | LargeCollection | upstream-utf16 | 475,717.0 ns |  2,640.25 ns |  2,204.73 ns | 228.5156 | 113.7695 |        - | 1913112 B |
| Serialize   | NestedOrder     | fbj-utf16      |   2,437.5 ns |      9.98 ns |      8.85 ns |   1.0147 |        - |        - |    8512 B |
| Deserialize | NestedOrder     | fbj-utf16      |   3,343.1 ns |     11.89 ns |     10.54 ns |   0.9842 |   0.0229 |        - |    8248 B |
| Serialize   | NestedOrder     | stj            |   2,459.5 ns |     50.98 ns |     47.69 ns |   0.4616 |        - |        - |    3864 B |
| Deserialize | NestedOrder     | stj            |   5,522.4 ns |     13.89 ns |     12.99 ns |   0.8392 |   0.0153 |        - |    7080 B |
| Serialize   | NestedOrder     | upstream-utf16 |   8,227.8 ns |     66.86 ns |     55.83 ns |   8.1177 |   0.4730 |        - |   68152 B |
| Deserialize | NestedOrder     | upstream-utf16 |   9,560.1 ns |    110.51 ns |     92.28 ns |   5.0354 |   0.5341 |        - |   42240 B |
```

## Notes

- The payloads are synthetic and small. Your own data decides the result, so measure it.
- System.Text.Json writes no type information, so it cannot read back a polymorphic graph without extra
  configuration. This package can, which these benchmarks do not measure.
- Timings on a shared CI runner are noisier than on an idle workstation. A .NET Framework 4.8 run is available
  through the `benchmark-net48` workflow.
- The size and gzip tables are in the README; the `sizes` command above prints them, along with the cases where this
  package's bytes differ from upstream's (for example a dictionary key of 256 encoded bytes or more).
