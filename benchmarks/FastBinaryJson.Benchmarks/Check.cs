using System;

namespace FastBinaryJson.Benchmarks
{
    /// <summary>
    /// Argument check that compiles on net10.0 and net48 alike; ArgumentNullException.ThrowIfNull needs .NET 6.
    /// </summary>
    internal static class Check
    {
        /// <summary>
        /// Throws if <paramref name="value"/> is null.
        /// </summary>
        /// <param name="value">The argument to check.</param>
        /// <param name="name">The parameter name to report.</param>
        /// <exception cref="ArgumentNullException">If <paramref name="value"/> is null.</exception>
        public static void NotNull([ValidatedNotNull] object? value, string name)
        {
            if (value == null)
            {
                throw new ArgumentNullException(name);
            }
        }
    }

    /// <summary>
    /// Tells CA1062 that the annotated argument has been checked by the callee.
    /// </summary>
    [AttributeUsage(AttributeTargets.Parameter)]
    internal sealed class ValidatedNotNullAttribute : Attribute { }
}
