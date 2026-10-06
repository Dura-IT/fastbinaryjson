using System.Collections.Generic;

namespace DuraIT.FastBinaryJson
{
    internal sealed class DatasetSchema
    {
        // Populated by the deserializer, so both are null on a freshly constructed instance.
        public List<string>? Info { get; set; }
        public string? Name { get; set; }
    }
}
