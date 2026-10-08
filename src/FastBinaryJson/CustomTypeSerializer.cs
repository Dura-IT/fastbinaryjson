namespace DuraIT.FastBinaryJson
{
    /// <summary>
    /// Turns a value of a registered custom type into the string stored in its place.
    /// </summary>
    /// <param name="data">The value to write.</param>
    /// <returns>The text to store.</returns>
    public delegate string CustomTypeSerializer(object data);
}
