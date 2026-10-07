namespace DuraIT.FastBinaryJson.Internal
{
    /// <summary>
    /// How the serializer writes a value of a given runtime type, decided once per type.
    /// </summary>
    /// <remarks>
    /// The members follow the order WriteValue has always tested them in, because that order is the
    /// behaviour: an ExpandoObject is a dictionary before it is a sequence, a registered type that is
    /// also a sequence is still written as a sequence, and so on. <see cref="Object"/> is what is left.
    /// </remarks>
    internal enum WriteKind
    {
        Object,
        ExpandoDictionary,
        StringKeyedDictionary,
        Dictionary,
        DataSet,
        DataTable,
        Bytes,
        StringDictionary,
        NameValueCollection,
        Array,
        Sequence,
        Enum,
        Custom,
        DateTimeOffset,
    }
}
