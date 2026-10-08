#if !NET5_0_OR_GREATER
namespace System.Runtime.CompilerServices
{
    /// <summary>
    /// Lets netstandard2.0 compile init-only members and records; the compiler looks the type up by name.
    /// </summary>
#pragma warning disable S2094 // The compiler only needs the type to exist; it has no members by design.
    internal static class IsExternalInit { }
#pragma warning restore S2094
}
#endif
