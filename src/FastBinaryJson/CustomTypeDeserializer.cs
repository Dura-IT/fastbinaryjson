namespace DuraIT.FastBinaryJson
{
    /// <summary>
    /// Turns the string stored for a registered custom type back into a value.
    /// </summary>
    /// <param name="data">The stored text.</param>
    /// <returns>The restored value.</returns>
    public delegate object CustomTypeDeserializer(string data);
}
