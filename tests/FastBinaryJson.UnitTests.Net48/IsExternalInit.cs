// ReSharper disable CheckNamespace - the compiler looks for this type in System.Runtime.CompilerServices
namespace System.Runtime.CompilerServices
{
    // net48 has no init-only setters or records in the box; the linked corpus uses both.
#pragma warning disable S2094 // A marker type: the compiler only looks for its name
    internal static class IsExternalInit { }
#pragma warning restore S2094
}
