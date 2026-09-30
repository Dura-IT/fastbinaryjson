using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Data;
using DuraIT.FastBinaryJson.Internal;

namespace DuraIT.FastBinaryJson
{
    /// <summary>
    /// Reads a typed <c>ToObject</c> straight from the bytes into the target objects, without first
    /// building the Dictionary&lt;string, object&gt; graph the two-step path maps from.
    /// </summary>
    /// <remarks>
    /// It is not a second implementation of the conversion rules. It constructs only plain objects and
    /// <c>List&lt;T&gt;</c> itself; every other value is read by the parser's own code and converted
    /// by <see cref="Deserializer.ConvertValue"/>, and instances are created, numbered for $i and
    /// typed through the same Deserializer members ParseDictionary uses. What it saves is the
    /// dictionary per object and the List&lt;object&gt; per collection, not any of the semantics.
    ///
    /// Anything it cannot reproduce exactly - an object with no $type where one is needed, a $ key
    /// after the head, a $types table anywhere but the head - is rewound and handed to the two-step
    /// path, with its $i numbering and $types entries undone first so that path sees the same state.
    ///
    /// One accepted difference: the parser throws on a key repeated within an object (Dictionary.Add);
    /// this reader applies both occurrences, so the last wins. No writer emits duplicate keys.
    /// </remarks>
    internal sealed class TypedReader
    {
        private readonly Deserializer _deserializer;
        private readonly BJsonParser _parser;

        private TypedReader(Deserializer deserializer, BJsonParser parser)
        {
            _deserializer = deserializer;
            _parser = parser;
        }

        /// <summary>
        /// Reads <paramref name="json"/> as <paramref name="type"/> when the root is a shape this
        /// reader handles; returns false, with no state changed, otherwise.
        /// </summary>
        internal static bool TryRead(Deserializer deserializer, byte[] json, Type type, Type? genericDefinition, out object? result)
        {
            result = null;
            if (json.Length == 0)
                return false;

            BJSONParameters parameters = deserializer.Parameters;
            TypedReader reader = new TypedReader(deserializer, new BJsonParser(json, parameters.UseUTCDateTime, parameters.v1_4TypedArray));

            // The same shapes Deserializer.ToObject sends to ParseDictionary and RootList.
            if (json[0] == TOKENS.DOC_START && IsPlainRootObject(type, genericDefinition))
                return reader.TryReadObject(type, null, false, out result);

            if (json[0] == TOKENS.ARRAY_START && genericDefinition == typeof(List<>))
            {
                result = reader.ReadRootList(type);
                return true;
            }

            return false;
        }

        private static bool IsPlainRootObject(Type type, Type? genericDefinition)
        {
            if (type.IsEnum || type == typeof(DataSet) || type == typeof(DataTable))
                return false;
            if (genericDefinition == typeof(Dictionary<,>) || genericDefinition == typeof(List<>))
                return false;

            return type == typeof(object) || typeof(IEnumerable).IsAssignableFrom(type) == false;
        }

        /// <summary>
        /// A member ConvertValue would build with ParseDictionary when handed a dictionary.
        /// </summary>
        private static bool IsPlainObjectMember(myPropInfo pi)
        {
            if (IsSpecialMember(pi))
                return false;
            if (pi.IsGenericType && pi.IsValueType == false)
                return false;

            return pi.IsClass || pi.IsStruct || pi.IsInterface;
        }

        /// <summary>
        /// A member ConvertValue would build with CreateGenericList when handed a list.
        /// </summary>
        private static bool IsPlainListMember(myPropInfo pi)
        {
            return IsSpecialMember(pi) == false && pi.IsGenericType && pi.IsValueType == false;
        }

        // The members ConvertValue's switch handles itself - always left to it.
        private static bool IsSpecialMember(myPropInfo pi)
        {
            switch (pi.Type)
            {
                case myPropInfoType.DataSet:
                case myPropInfoType.DataTable:
                case myPropInfoType.Custom:
                case myPropInfoType.Enum:
                case myPropInfoType.SByte:
                case myPropInfoType.StringKeyDictionary:
                case myPropInfoType.Hashtable:
                case myPropInfoType.Dictionary:
                case myPropInfoType.NameValue:
                case myPropInfoType.StringDictionary:
                case myPropInfoType.Array:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// The equivalent of ParseDictionary(dictionary, globaltypes, declared, null) for the object at
        /// the current index. Returns false with the index back on its DOC_START, and every side effect
        /// undone, when the object has to go through the two-step path.
        /// </summary>
        /// <param name="mayExtendSharedTypes">
        /// True only for an element of a root list, whose $types table lands in the list's shared one.
        /// </param>
        private bool TryReadObject(Type? declared, Dictionary<string, object>? globaltypes, bool mayExtendSharedTypes, out object? result)
        {
            result = null;
            if (declared == typeof(NameValueCollection) || declared == typeof(StringDictionary))
            {
                _deserializer.OneStepFallbacks++;
                return false;
            }

            int start = _parser.Index;
            int circular = _deserializer.CircularCount;
            Dictionary<string, object>? sharedTypes = globaltypes;
            List<string>? addedTypes = null;
            bool readTypes = false;

            _parser.ReadToken(); // DOC_START
            byte t = ReadSkippingCommas();

            if (t == TOKENS.TYPES_POINTER)
            {
                if (globaltypes != null && (mayExtendSharedTypes == false || globaltypes.Count > 0))
                    return Abandon(start, circular, sharedTypes, addedTypes);

                Dictionary<string, object> table = _parser.ReadTypesTable();
                if (table.Count != 1 || table.TryGetValue("$types", out object? types) == false)
                    return Abandon(start, circular, sharedTypes, addedTypes);

                if (globaltypes == null)
                    globaltypes = new Dictionary<string, object>();
                else
                    addedTypes = new List<string>();

                foreach (KeyValuePair<string, object> kv in (Dictionary<string, object>)types)
                {
                    globaltypes.Add(kv.Key, kv.Value);
                    addedTypes?.Add(kv.Key);
                }

                readTypes = true;
                t = ReadSkippingCommas();
            }

            Type? type = declared;
            string? firstMember = null;
            bool needsDeclaredType = true;
            if (t != TOKENS.DOC_END)
            {
                string name = _parser.ReadName(t);
                _parser.ReadColon();

                if (name == "$i")
                {
                    // ParseDictionary resolves $i before it merges $types, so it never merges both.
                    if (readTypes)
                        return Abandon(start, circular, sharedTypes, addedTypes);

                    object? id = _parser.ReadValue(out bool broke);
                    if (broke || _parser.PeekToken() != TOKENS.DOC_END)
                        return Abandon(start, circular, sharedTypes, addedTypes);

                    _parser.ReadToken();
                    result = _deserializer.ResolveCircular(id!);
                    return true;
                }

                if (name == "$type")
                {
                    object? tn = _parser.ReadValue(out bool broke);
                    if (broke)
                        return Abandon(start, circular, sharedTypes, addedTypes);

                    type = Deserializer.ResolveType(tn!, globaltypes);
                    needsDeclaredType = false;
                }
                else if (IsSpecialName(name))
                {
                    return Abandon(start, circular, sharedTypes, addedTypes);
                }
                else
                {
                    firstMember = name;
                }
            }

            // No $type: ParseDictionary returns the dictionary itself for object, and fails for the rest.
            if (needsDeclaredType && (type == null || type == typeof(object) || type.IsAbstract || type.IsInterface))
                return Abandon(start, circular, sharedTypes, addedTypes);
            if (type == null)
                return Abandon(start, circular, sharedTypes, addedTypes);

            object o = _deserializer.CreateInstance(type);
            _deserializer.RegisterCircular(o);
            WireNameMap members = Reflection.Instance.GetWireNameMap(type, type.FullName!, _deserializer.Parameters.ShowReadOnlyProperties);

            if (t == TOKENS.DOC_END)
            {
                result = o;
                return true;
            }

            bool ended = false;
            if (firstMember != null)
                o = ReadMember(o, members, firstMember, globaltypes, out ended);

            while (ended == false)
            {
                t = _parser.ReadToken();
                if (t == TOKENS.COMMA)
                    continue;
                if (t == TOKENS.DOC_END)
                    break;
                if (t == TOKENS.TYPES_POINTER)
                    return Abandon(start, circular, sharedTypes, addedTypes);

                string name = _parser.ReadName(t);
                _parser.ReadColon();
                if (IsSpecialName(name))
                    return Abandon(start, circular, sharedTypes, addedTypes);

                o = ReadMember(o, members, name, globaltypes, out ended);
            }

            result = o;
            return true;
        }

        /// <summary>
        /// Reads one member's value and applies it, as ParseDictionary's loop does for one entry.
        /// </summary>
        /// <param name="ended">
        /// Set when the value position held a structural token instead - the parser ends the object
        /// there, and so does this.
        /// </param>
        private object ReadMember(object o, WireNameMap members, string name, Dictionary<string, object>? globaltypes, out bool ended)
        {
            ended = false;
            myPropInfo? pi = members.Find(name);

            if (pi != null && pi.CanWrite)
            {
                byte next = _parser.PeekToken();
                if (next == TOKENS.DOC_START && IsPlainObjectMember(pi))
                {
                    object? value;
                    if (TryReadObject(pi.pt, globaltypes, false, out object? read))
                        value = read;
                    else
                        value = _deserializer.ConvertValue(pi, _parser.ReadValue(out _)!, globaltypes);

                    return pi.setter!(o, value!);
                }

                if (next == TOKENS.ARRAY_START && IsPlainListMember(pi))
                {
                    _parser.ReadToken();
                    return pi.setter!(o, ReadGenericList(pi.pt, pi.bt, globaltypes));
                }
            }

            object? v = _parser.ReadValue(out bool broke);
            if (broke)
            {
                ended = true;
                return o;
            }

            if (pi != null && pi.CanWrite && v != null)
                return pi.setter!(o, _deserializer.ConvertValue(pi, v, globaltypes)!);

            return o;
        }

        /// <summary>
        /// The equivalent of CreateGenericList for the list whose ARRAY_START was just read.
        /// </summary>
        private object ReadGenericList(Type pt, Type? bt, Dictionary<string, object>? globaltypes)
        {
            List<object?> items = ReadElements(bt, globaltypes, false, ob =>
            {
                if (ob is List<object> list)
                    return bt!.IsGenericType ? list : list.ToArray();
                if (ob is TypedArray typed)
                    return typed.data.ToArray();

                return ob;
            });

            // Created after the elements are read, so it gets the capacity CreateGenericList gives it.
            IList col = (IList)Reflection.Instance.FastCreateList(pt, items.Count);
            foreach (object? item in items)
                col.Add(item);

            return col;
        }

        /// <summary>
        /// The equivalent of RootList for a root ARRAY_START.
        /// </summary>
        private object ReadRootList(Type type)
        {
            Type[] gtypes = Reflection.Instance.GetGenericArguments(type);
            // Shared by every element, as in RootList: the first element's $types table lands here.
            Dictionary<string, object> globals = new Dictionary<string, object>();

            _parser.ReadToken(); // ARRAY_START
            List<object?> items = ReadElements(gtypes[0], globals, true, ob => ob);

            IList o = (IList)Reflection.Instance.FastCreateList(type, items.Count);
            foreach (object? item in items)
                o.Add(item);

            return o;
        }

        /// <summary>
        /// Reads the elements of an array whose ARRAY_START was just read, with ParseArray's loop.
        /// Object elements become <paramref name="bt"/> as ParseDictionary would make them; every
        /// other element is materialised by the parser and passed through <paramref name="convert"/>.
        /// </summary>
        private List<object?> ReadElements(Type? bt, Dictionary<string, object>? globaltypes, bool mayExtendSharedTypes, Func<object?, object?> convert)
        {
            List<object?> items = new List<object?>();
            bool broke = false;
            while (broke == false)
            {
                object? item;
                // An object element is added as built, never through convert - as in both originals.
                bool isObject = _parser.PeekToken() == TOKENS.DOC_START;
                if (isObject)
                {
                    if (TryReadObject(bt, globaltypes, mayExtendSharedTypes, out object? read) == false)
                        read = _deserializer.ParseDictionary((Dictionary<string, object>)_parser.ReadValue(out _)!, globaltypes, bt, null);
                    item = read;
                }
                else
                {
                    item = _parser.ReadValue(out broke);
                }

                byte t;
                if (broke == false)
                {
                    items.Add(isObject ? item : convert(item));
                    t = _parser.ReadToken();
                }
                else
                {
                    t = (byte)item!;
                }

                if (t == TOKENS.COMMA)
                    continue;
                if (t == TOKENS.ARRAY_END)
                    break;
            }

            return items;
        }

        private byte ReadSkippingCommas()
        {
            byte t = _parser.ReadToken();
            while (t == TOKENS.COMMA)
                t = _parser.ReadToken();

            return t;
        }

        // $type, $types, $i, $schema - anything the two-step path might treat specially.
        private static bool IsSpecialName(string name) => name.Length > 0 && name[0] == '$';

        private bool Abandon(int start, int circular, Dictionary<string, object>? sharedTypes, List<string>? addedTypes)
        {
            _deserializer.OneStepFallbacks++;
            _deserializer.UndoCircular(circular);
            if (sharedTypes != null && addedTypes != null)
            {
                foreach (string key in addedTypes)
                    sharedTypes.Remove(key);
            }

            _parser.Index = start;
            return false;
        }
    }
}
