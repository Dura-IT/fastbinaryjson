using System.Collections.Generic;

namespace DuraIT.FastBinaryJson
{
    public sealed class DatasetSchema
    {
        // Populated by the deserializer, so both are null on a freshly constructed instance.
        public List<string>? Info;
        public string? Name;
    }
}
