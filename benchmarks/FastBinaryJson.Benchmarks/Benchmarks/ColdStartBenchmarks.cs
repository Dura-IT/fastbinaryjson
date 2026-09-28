using BenchmarkDotNet.Attributes;

namespace FastBinaryJson.Benchmarks.Benchmarks
{
    /// <summary>
    /// First-call-per-type cost. fastBinaryJSON builds getters and setters with Reflection.Emit
    /// and caches them per type, so a steady-state benchmark amortises that emit away to nothing
    /// and reports a number no short-lived process will ever see. Clearing the reflection cache
    /// before each single invocation exposes it.
    ///
    /// InvocationCount and UnrollFactor are pinned to 1 because [IterationSetup] must run for
    /// exactly one measured operation, otherwise the emit cost is amortised again.
    /// </summary>
    [MemoryDiagnoser]
    [SimpleJob(launchCount: 3, warmupCount: 2, iterationCount: 10, invocationCount: 1)]
    public class ColdStartBenchmarks
    {
        private PayloadCase _payload = null!;
        private ISerializerArm _arm = null!;

        [Params("FlatPrimitives", "NestedOrder", "LargeCollection", "GuidDense", "Polymorphic")]
        public string Payload { get; set; } = string.Empty;

        [GlobalSetup]
        public void Setup()
        {
            _payload = Payloads.ByName(Payload);
            _arm = Payloads.ArmByKey("fbj-utf16");
        }

        [IterationSetup]
        public void ClearCache() => global::fastBinaryJSON.BJSON.ClearReflectionCache();

        [Benchmark(Description = "First serialize after cache clear")]
        public byte[] ColdSerialize() => _payload.Serialize(_arm);
    }

}
