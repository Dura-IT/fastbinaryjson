using System;
using System.Collections.Generic;
using System.Linq;
using AwesomeAssertions;
using DuraIT.FastBinaryJson;
using NUnit.Framework;

namespace FastBinaryJson.UnitTests.RoundTrip
{
    /*
     * A decimal is written as the token followed by decimal.GetBits in order: low, middle, high, flags.
     * netstandard2.0 has no span overload of GetBits, so it reads the value's memory when that layout is the
     * known one; these tests pin the bytes on whatever runtime they run on, whichever path it takes.
     */
    [TestFixture]
    [TestOf(typeof(Bjson))]
    public sealed class DecimalWireTests
    {
        private static IEnumerable<decimal> Values()
        {
            yield return 0m;
            yield return 1m;
            yield return -1m;
            yield return 123.456m;
            yield return -0.0000000000000000000000000001m;
            yield return decimal.MaxValue;
            yield return decimal.MinValue;
            yield return 79228162514264337593543950335m / 3;
            yield return 1.2345678901234567890123456789m;
            yield return 100.00m;
        }

        [TestCaseSource(nameof(Values))]
        public void ToBjson_Decimal_WritesGetBitsInOrder(decimal value)
        {
            byte[] bytes = Bjson.ToBjson(value);

            byte[] expected = decimal.GetBits(value).SelectMany(BitConverter.GetBytes).ToArray();
            int start = Array.IndexOf(bytes, Tokens.Decimal);
            bytes.AsSpan(start + 1, 16).ToArray().Should().Equal(expected);
        }

        [TestCaseSource(nameof(Values))]
        public void ToObject_Decimal_RoundTripsExactly(decimal value)
        {
            Bjson.ToObject<decimal>(Bjson.ToBjson(value)).Should().Be(value);
        }
    }
}
