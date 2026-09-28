using System;
using System.Text.Json;
using MessagePack;
using MessagePack.Resolvers;

namespace FastBinaryJson.Benchmarks
{
    /// <summary>
    /// One serializer configuration under measurement.
    /// </summary>
    public interface ISerializerArm
    {
        /// <summary>
        /// Short label used in every report column.
        /// </summary>
        string Name { get; }

        byte[] Serialize<T>(T value);

        T Deserialize<T>(byte[] bytes);
    }

    /// <summary>
    /// fastBinaryJSON at its shipped defaults, parameterised by the one built-in size/speed
    /// lever the library exposes: UseUnicodeStrings (UTF-16, faster) versus UTF-8 (smaller).
    /// </summary>
    public sealed class FastBinaryJsonArm : ISerializerArm
    {
        private readonly global::fastBinaryJSON.BJSONParameters _parameters;
        private readonly string _name;

        public FastBinaryJsonArm(bool useUnicodeStrings)
        {
            _name = useUnicodeStrings ? "fastBinaryJSON (UTF-16)" : "fastBinaryJSON (UTF-8)";
            _parameters = new global::fastBinaryJSON.BJSONParameters
            {
                UseUnicodeStrings = useUnicodeStrings,
            };
        }

        public string Name => _name;

        public byte[] Serialize<T>(T value) => global::fastBinaryJSON.BJSON.ToBJSON(value, _parameters);

        public T Deserialize<T>(byte[] bytes) => global::fastBinaryJSON.BJSON.ToObject<T>(bytes, _parameters);
    }

    /// <summary>
    /// System.Text.Json at defaults - the in-box text baseline every reader will compare against.
    /// </summary>
    public sealed class SystemTextJsonArm : ISerializerArm
    {
        private readonly JsonSerializerOptions _options;

        public SystemTextJsonArm()
        {
            _options = new JsonSerializerOptions(JsonSerializerDefaults.General);
        }

        public string Name => "System.Text.Json";

        public byte[] Serialize<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value, _options);

        public T Deserialize<T>(byte[] bytes) => JsonSerializer.Deserialize<T>(bytes, _options)!;
    }

    /// <summary>
    /// MessagePack with the contractless resolver, i.e. no attributes on the corpus types - the
    /// closest equivalent to how fastBinaryJSON is used. Note this resolver does not embed type
    /// information, so it cannot reconstruct a polymorphic graph; the compatibility matrix
    /// reports that rather than hiding it.
    /// </summary>
    public sealed class MessagePackArm : ISerializerArm
    {
        private readonly MessagePackSerializerOptions _options;

        public MessagePackArm()
        {
            _options = MessagePackSerializerOptions.Standard.WithResolver(ContractlessStandardResolver.Instance);
        }

        public string Name => "MessagePack";

        public byte[] Serialize<T>(T value) => MessagePackSerializer.Serialize(value, _options);

        public T Deserialize<T>(byte[] bytes) => MessagePackSerializer.Deserialize<T>(bytes, _options);
    }
}
