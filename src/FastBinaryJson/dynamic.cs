using System;
using System.Collections;
using System.Collections.Generic;
using System.Dynamic;

namespace DuraIT.FastBinaryJson
{
    internal class DynamicJson : DynamicObject, IEnumerable
    {
        // Exactly one of these is populated, decided by what Parse returned - so both are
        // genuinely nullable, and every use below asserts the one the caller implied.
        private IDictionary<string, object>? _dictionary { get; set; }
        private List<object>? _list { get; set; }

        public DynamicJson(byte[] json)
        {
            var parse = BJSON.Parse(json);

            if (parse is IDictionary<string, object> parsedDictionary)
                _dictionary = parsedDictionary;
            else if (parse is TypedArray typedArray)
                _list = typedArray.data;
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
                result = _dictionary![(string)index];
            }
            if (result is IDictionary<string, object> resultDictionary)
                result = new DynamicJson(resultDictionary);
            return true;
        }

        private bool TryGetIgnoringCase(string name, out object? value)
        {
            foreach (KeyValuePair<string, object> entry in _dictionary!)
            {
                if (string.Equals(entry.Key, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = entry.Value;
                    return true;
                }
            }

            value = null;
            return false;
        }

        public override bool TryGetMember(GetMemberBinder binder, out object? result)
        {
            if (!_dictionary!.TryGetValue(binder.Name, out result) && !TryGetIgnoringCase(binder.Name, out result))
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
