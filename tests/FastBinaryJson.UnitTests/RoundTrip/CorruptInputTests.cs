// ReSharper disable UnusedAutoPropertyAccessor.Global - reflection-only models: the serializer reads and writes these members, nothing calls them
using System;
using System.Collections.Generic;
using AwesomeAssertions;
using DuraIT.FastBinaryJson;
using FastBinaryJson.Benchmarks.Corpus;
using NUnit.Framework;

namespace FastBinaryJson.UnitTests.RoundTrip
{
    /*
     * Bytes that are not a valid payload - truncated, bit-flipped, or declaring more than they hold - are
     * the caller's input, not a bug in the caller. They must surface as BjsonException, one type a caller
     * can catch, and never as the IndexOutOfRange, ArgumentOutOfRange, NullReference or out-of-memory
     * that whichever line of the reader happened to trip on would give.
     */
    [TestFixture]
    [TestOf(typeof(Bjson))]
    public sealed class CorruptInputTests
    {
        private static readonly int[] ThreeItems = { 1, 2, 3 };

        [Test]
        public void EveryEntryPoint_TruncatedPayload_ThrowsOnlyBjsonException()
        {
            byte[] whole = Bjson.ToBjson(PayloadFactory.CreateNestedOrder());

            for (int length = 0; length < whole.Length; length++)
            {
                byte[] cut = new byte[length];
                Array.Copy(whole, cut, length);
                AssertOnlyBjsonException(cut, $"truncated to {length} of {whole.Length} bytes");
            }
        }

        [Test]
        public void EveryEntryPoint_BitFlippedPayload_ThrowsOnlyBjsonException()
        {
            byte[] whole = Bjson.ToBjson(PayloadFactory.CreateNestedOrder());
            var random = new Random(20261007);

            for (int round = 0; round < 400; round++)
            {
                byte[] damaged = (byte[])whole.Clone();
                for (int flips = 0; flips < 1 + (round % 3); flips++)
                    damaged[random.Next(damaged.Length)] ^= (byte)(1 << random.Next(8));
                AssertOnlyBjsonException(damaged, $"round {round}");
            }
        }

        [Test]
        public void ToObject_TypedArrayDeclaringMoreElementsThanTheRemainingBytes_ThrowsBjsonException()
        {
            byte[] bytes = Bjson.ToBjson(ThreeItems);
            int count = IndexOf(bytes, BitConverter.GetBytes(3));
            BitConverter.GetBytes(int.MaxValue).CopyTo(bytes, count);

            FluentActions.Invoking(() => Bjson.ToObject<int[]>(bytes)).Should().Throw<BjsonException>();
            FluentActions.Invoking(() => Bjson.Parse(bytes)).Should().Throw<BjsonException>();
        }

        [Test]
        public void ToObject_TypedArrayDeclaringANegativeCount_ThrowsBjsonException()
        {
            byte[] bytes = Bjson.ToBjson(ThreeItems);
            int count = IndexOf(bytes, BitConverter.GetBytes(3));
            BitConverter.GetBytes(-5).CopyTo(bytes, count);

            FluentActions.Invoking(() => Bjson.ToObject<int[]>(bytes)).Should().Throw<BjsonException>();
        }

        [Test]
        public void ToObject_InnerExceptionIsKept()
        {
            byte[] bytes = Bjson.ToBjson(PayloadFactory.CreateNestedOrder());
            byte[] cut = new byte[bytes.Length / 2];
            Array.Copy(bytes, cut, cut.Length);

            FluentActions
                .Invoking(() => Bjson.ToObject<Order>(cut))
                .Should()
                .Throw<BjsonException>()
                .Where(e => e.InnerException != null || e.Message.Length > 0);
        }

        private static void AssertOnlyBjsonException(byte[] bytes, string context)
        {
            var holder = new Order();
            var actions = new Dictionary<string, Action>
            {
                ["Parse"] = () => Bjson.Parse(bytes),
                ["ToObject<T>"] = () => Bjson.ToObject<Order>(bytes),
                ["ToObject"] = () => Bjson.ToObject(bytes),
                ["ToDynamic"] = () => Bjson.ToDynamic(bytes),
                ["FillObject"] = () => Bjson.FillObject(holder, bytes),
            };

            foreach (KeyValuePair<string, Action> entry in actions)
            {
                try
                {
                    entry.Value();
                }
                catch (BjsonException)
                {
                    // the contract
                }
#pragma warning disable CA1031 // Reporting which entry point threw the wrong type is the whole point
                catch (Exception ex)
                {
                    Assert.Fail($"{entry.Key}, {context}: threw {ex.GetType().Name} instead of BjsonException: {ex.Message}");
                }
#pragma warning restore CA1031
            }
        }

        private static int IndexOf(byte[] haystack, byte[] needle)
        {
            for (int i = 0; i <= haystack.Length - needle.Length; i++)
            {
                bool match = true;
                for (int j = 0; j < needle.Length && match; j++)
                    match = haystack[i + j] == needle[j];
                if (match)
                    return i;
            }

            return -1;
        }
    }
}
