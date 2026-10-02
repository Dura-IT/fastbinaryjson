using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace DuraIT.FastBinaryJson.Internal
{
    /// <summary>
    /// Compares objects by identity, never by their own Equals.
    /// </summary>
    /// <remarks>
    /// The same thing as ReferenceEqualityComparer, which netstandard2.0 does not have; one class for
    /// both targets keeps the writer's $i bookkeeping identical on each.
    /// </remarks>
    internal sealed class ReferenceComparer : IEqualityComparer<object>
    {
        internal static readonly ReferenceComparer Instance = new ReferenceComparer();

        private ReferenceComparer()
        {
        }

        public new bool Equals(object? x, object? y) => ReferenceEquals(x, y);

        public int GetHashCode(object obj) => RuntimeHelpers.GetHashCode(obj);
    }
}
