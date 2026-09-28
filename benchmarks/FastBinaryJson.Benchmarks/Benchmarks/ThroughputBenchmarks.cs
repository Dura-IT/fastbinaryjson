using BenchmarkDotNet.Attributes;

namespace FastBinaryJson.Benchmarks.Benchmarks
{
    /*
     * LaunchCount is deliberately 3, not BenchmarkDotNet's ShortRun default of 1. With a single
     * launch the reported StdDev is within-process only - JIT, GC and alignment fate is shared
     * across every iteration, so error bars look tight while the true spread across processes is
     * far wider. Any small-magnitude ordering read off a single-launch run is unsafe.
     *
     * MemoryDiagnoser reports managed allocations only; native allocations are invisible to it.
     */
    /// <summary>
    /// Steady-state serialize and deserialize across the four payload shapes every arm can
    /// round-trip. The polymorphic payload is measured separately because MessagePack's
    /// contractless resolver cannot encode it at all.
    /// </summary>
    [MemoryDiagnoser]
    [SimpleJob(launchCount: 3, warmupCount: 3, iterationCount: 5)]
    public class ThroughputBenchmarks
    {
        private PayloadCase _payload = null!;
        private ISerializerArm _arm = null!;
        private byte[] _encoded = null!;

        [Params("FlatPrimitives", "NestedOrder", "LargeCollection", "GuidDense")]
        public string Payload { get; set; } = string.Empty;

        [Params("fbj-utf16", "fbj-utf8", "stj", "msgpack")]
        public string Arm { get; set; } = string.Empty;

        [GlobalSetup]
        public void Setup()
        {
            _payload = Payloads.ByName(Payload);
            _arm = Payloads.ArmByKey(Arm);
            _encoded = _payload.Serialize(_arm);
        }

        [Benchmark]
        public byte[] Serialize() => _payload.Serialize(_arm);

        [Benchmark]
        public object Deserialize() => _payload.Deserialize(_arm, _encoded);
    }
}
