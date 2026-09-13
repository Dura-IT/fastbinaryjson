using System;

using fastBinaryJSON;

namespace FastBinaryJson.UnitTests.Golden
{
    /// <summary>
    /// One pinned wire-format case: a deterministic value, the parameters it is written with, and
    /// the typed read-back used to prove the committed bytes still decode to that value.
    /// </summary>
    /// <remarks>
    /// Public only because NUnit requires a public parameter type on a [TestCaseSource] method.
    /// </remarks>
    public sealed record GoldenCase(
        string Name,
        Func<object> Build,
        Func<BJSONParameters> Parameters,
        Func<byte[], BJSONParameters, object> Deserialize,
        string BytesOnlyReason)
    {
        /// <summary>
        /// Builds a case whose read-back is bound to the concrete type <typeparamref name="T"/>.
        /// </summary>
        public static GoldenCase For<T>(string name, Func<T> build, Func<BJSONParameters> parameters, string bytesOnlyReason = null)
        {
            return new GoldenCase(
                name,
                () => build(),
                parameters,
                (bytes, param) => BJSON.ToObject<T>(bytes, param),
                bytesOnlyReason);
        }

        public override string ToString()
        {
            return Name;
        }
    }
}
