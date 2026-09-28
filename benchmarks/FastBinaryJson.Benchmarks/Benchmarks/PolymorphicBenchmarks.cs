using BenchmarkDotNet.Attributes;

namespace FastBinaryJson.Benchmarks.Benchmarks
{
    /// <summary>
    /// The polymorphic graph, which is fastBinaryJSON's actual differentiator. MessagePack is
    /// absent by necessity rather than by choice: the contractless resolver carries no type
    /// information and throws on an abstract member, as the compatibility matrix records.
    /// </summary>
    [MemoryDiagnoser]
    [SimpleJob(launchCount: 3, warmupCount: 3, iterationCount: 5)]
    public class PolymorphicBenchmarks
    {
        private PayloadCase _payload = null!;
        private ISerializerArm _arm = null!;
        private byte[] _encoded = null!;

        [Params("fbj-utf16", "fbj-utf8", "stj")]
        public string Arm { get; set; } = string.Empty;

        [GlobalSetup]
        public void Setup()
        {
            _payload = Payloads.ByName("Polymorphic");
            _arm = Payloads.ArmByKey(Arm);
            _encoded = _payload.Serialize(_arm);
        }

        [Benchmark]
        public byte[] Serialize() => _payload.Serialize(_arm);

        [Benchmark]
        public object Deserialize() => _payload.Deserialize(_arm, _encoded);
    }
}
