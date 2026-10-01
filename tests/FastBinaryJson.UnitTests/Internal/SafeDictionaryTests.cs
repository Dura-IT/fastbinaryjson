using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using AwesomeAssertions;

using DuraIT.FastBinaryJson.Internal;

using NUnit.Framework;

namespace FastBinaryJson.UnitTests.Internal
{
    [TestFixture]
    [TestOf(typeof(SafeDictionary<,>))]
    public sealed class SafeDictionaryTests
    {
        [Test]
        public void Add_ExistingKey_KeepsTheFirstValue()
        {
            SafeDictionary<string, int> dictionary = new SafeDictionary<string, int>();

            dictionary.Add("a", 1);
            dictionary.Add("a", 2);

            dictionary["a"].Should().Be(1);
            dictionary.Count().Should().Be(1);
        }

        [Test]
        public void Indexer_ExistingKey_ReplacesWithoutCountingTwice()
        {
            SafeDictionary<string, int> dictionary = new SafeDictionary<string, int>(10);

            dictionary["a"] = 1;
            dictionary["a"] = 2;

            dictionary["a"].Should().Be(2);
            dictionary.Count().Should().Be(1);
        }

        [Test]
        public void TryGetValue_MissingKey_ReturnsFalseAndDefault()
        {
            SafeDictionary<string, string?> dictionary = new SafeDictionary<string, string?>();

            dictionary.TryGetValue("missing", out string? value).Should().BeFalse();
            value.Should().BeNull();
        }

        [Test]
        public void TryGetValue_NullValue_IsAHit()
        {
            SafeDictionary<string, string?> dictionary = new SafeDictionary<string, string?>();
            dictionary.Add("cached miss", null);

            dictionary.TryGetValue("cached miss", out string? value).Should().BeTrue();
            value.Should().BeNull();
        }

        [Test]
        public void Indexer_MissingKey_ThrowsKeyNotFound()
        {
            SafeDictionary<string, int> dictionary = new SafeDictionary<string, int>();

            FluentActions.Invoking(() => dictionary["missing"]).Should().Throw<KeyNotFoundException>();
        }

        /// <summary>
        /// The count is kept by hand, so it has to stay exact when the same keys race in from many threads.
        /// </summary>
        [Test]
        public void Add_SameKeysFromManyThreads_CountsEachKeyOnce()
        {
            SafeDictionary<int, int> dictionary = new SafeDictionary<int, int>();

            Parallel.For(0, 64, worker =>
            {
                foreach (int key in Enumerable.Range(0, 1000))
                    dictionary.Add(key, worker);
            });

            dictionary.Count().Should().Be(1000);
        }
    }
}
