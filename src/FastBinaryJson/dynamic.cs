using System;
using System.Collections;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;

namespace DuraIT.FastBinaryJson
{
    internal sealed class DynamicJson : DynamicObject, IEnumerable
    {
        // Exactly one of these is populated, decided by what Parse returned - so both are
        // genuinely nullable, and every use below asserts the one the caller implied.
        private readonly IDictionary<string, object>? _dictionary;
        private readonly IList<object>? _list;

        public DynamicJson(byte[] json)
        {
            var parse = Bjson.Parse(json);

            if (parse is IDictionary<string, object> parsedDictionary)
                _dictionary = parsedDictionary;
            else if (parse is TypedArray typedArray)
                _list = typedArray.Data;
            else
                _list = (List<object>?)parse;
        }

        private DynamicJson(object? dictionary)
        {
            if (dictionary is IDictionary<string, object> typedDictionary)
                _dictionary = typedDictionary;
        }

        public override bool TryGetIndex(GetIndexBinder binder, Object[] indexes, out Object? result)
        {
            var index = indexes[0];
            if (index is int position)
            {
                result = _list![position];
            }
            else
            {
                string key = (string)index;
                if (!TryGetValue(key, out result))
                    throw new KeyNotFoundException(key);
            }
            if (result is IDictionary<string, object> resultDictionary)
                result = new DynamicJson(resultDictionary);
            return true;
        }

        /*
         * Exact name first (one hash probe), then a scan ignoring case. The scan only runs on a miss and
         * allocates only a closure; with keys that differ only by case, the first in enumeration order wins.
         */
        private bool TryGetValue(string name, out object? value)
        {
            if (_dictionary!.TryGetValue(name, out value))
                return true;

            KeyValuePair<string, object> match = _dictionary.FirstOrDefault(e => string.Equals(e.Key, name, StringComparison.OrdinalIgnoreCase));
            value = match.Value;
            return match.Key != null;
        }

        public override bool TryGetMember(GetMemberBinder binder, out object? result)
        {
            if (!TryGetValue(binder.Name, out result))
                return false;

            if (result is IDictionary<string, object> memberDictionary)
            {
                result = new DynamicJson(memberDictionary);
            }
            else if (result is List<object> memberList)
            {
                List<object> list = new List<object>();
                foreach (object item in memberList)
                {
                    if (item is IDictionary<string, object> itemDictionary)
                        list.Add(new DynamicJson(itemDictionary));
                    else
                        list.Add(item);
                }
                result = list;
            }

            return true;
        }

        public IEnumerator GetEnumerator()
        {
            foreach (var o in _list!)
            {
                yield return new DynamicJson(o as IDictionary<string, object>);
            }
        }
    }
}
