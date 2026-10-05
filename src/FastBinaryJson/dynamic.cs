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

            if (parse is IDictionary<string, object>)
                _dictionary = (IDictionary<string, object>)parse;
            else if (parse is TypedArray)
                _list = ((TypedArray)parse).data;
            else
                _list = (List<object>?)parse;
        }

        private DynamicJson(object? dictionary)
        {
            if (dictionary is IDictionary<string, object>)
                _dictionary = (IDictionary<string, object>)dictionary;
        }

        public override bool TryGetIndex(GetIndexBinder binder, Object[] indexes, out Object? result)
        {
            var index = indexes[0];
            if (index is int)
            {
                result = _list![(int)index];
            }
            else
            {
                result = _dictionary![(string)index];
            }
            if (result is IDictionary<string, object>)
                result = new DynamicJson(result as IDictionary<string, object>);
            return true;
        }

        public override bool TryGetMember(GetMemberBinder binder, out object? result)
        {
            if (!_dictionary!.TryGetValue(binder.Name, out result))
                if (!_dictionary.TryGetValue(binder.Name.ToLowerInvariant(), out result))
                    return false; // throw new Exception("property not found " + binder.Name);

            if (result is IDictionary<string, object>)
            {
                result = new DynamicJson(result as IDictionary<string, object>);
            }
            else if (result is List<object>)
            {
                List<object> list = new List<object>();
                foreach (object item in (List<object>)result)
                {
                    if (item is IDictionary<string, object>)
                        list.Add(new DynamicJson(item as IDictionary<string, object>));
                    else
                        list.Add(item);
                }
                result = list;
            }

            return _dictionary!.ContainsKey(binder.Name);
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
