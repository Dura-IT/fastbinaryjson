namespace DuraIT.FastBinaryJson.Internal
{
    /// <summary>
    /// The member names the format itself reserves, as they appear on the wire.
    /// </summary>
    internal static class WireKeys
    {
        /// <summary>
        /// The type of the object it heads, or an id into the $types table.
        /// </summary>
        public const string Type = "$type";

        /// <summary>
        /// The schema carried by a DataSet or DataTable.
        /// </summary>
        public const string Schema = "$schema";
    }
}
