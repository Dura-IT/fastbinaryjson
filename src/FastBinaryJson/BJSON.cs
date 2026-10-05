using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Data;
using System.IO;
using DuraIT.FastBinaryJson.Internal;

namespace DuraIT.FastBinaryJson
{
    public sealed class TOKENS
    {
        public const byte DOC_START = 1;
        public const byte DOC_END = 2;
        public const byte ARRAY_START = 3;
        public const byte ARRAY_END = 4;
        public const byte COLON = 5;
        public const byte COMMA = 6;
        public const byte NAME = 7;
        public const byte STRING = 8;
        public const byte BYTE = 9;
        public const byte INT = 10;
        public const byte UINT = 11;
        public const byte LONG = 12;
        public const byte ULONG = 13;
        public const byte SHORT = 14;
        public const byte USHORT = 15;
        public const byte DATETIME = 16;
        public const byte GUID = 17;
        public const byte DOUBLE = 18;
        public const byte FLOAT = 19;
        public const byte DECIMAL = 20;
        public const byte CHAR = 21;
        public const byte BYTEARRAY = 22;
        public const byte NULL = 23;
        public const byte TRUE = 24;
        public const byte FALSE = 25;
        public const byte UNICODE_STRING = 26;
        public const byte DATETIMEOFFSET = 27;
        public const byte ARRAY_TYPED = 28;
        public const byte TYPES_POINTER = 29;
        public const byte TIMESPAN = 30;
        public const byte ARRAY_TYPED_LONG = 31;
        public const byte NAME_UNI = 32;

        /*
         * Added by this fork. Upstream stops at 32, so 33 onwards were free.
         *
         * SBYTE exists because WriteSByte emitted BYTE after casting, which left sbyte and byte
         * indistinguishable on the wire with no way for a reader to recover the sign. A stream
         * written before this token still reads: BYTE assigned to an sbyte property is
         * reinterpreted. The reverse does not hold - upstream rejects this token as unknown.
         */
        public const byte SBYTE = 33;

        /*
         * The long form of NAME and NAME_UNI, carrying a four-byte length instead of one, which is
         * how WriteString has always carried its own. WriteName put the encoded length in a single
         * byte and wrote `b.Length % 256` bytes, so every name of 256 encoded bytes or more was
         * silently truncated - 128 characters at the default UseUnicodeStrings = true.
         *
         * Written only from 256 encoded bytes onwards, so every name that survived before is still
         * encoded exactly as it was. Only input that was already being corrupted produces these
         * tokens, which is why upstream not accepting them costs nothing.
         */
        public const byte NAME_LONG = 34;
        public const byte NAME_UNI_LONG = 35;
    }

    public class TypedArray
    {
        public string typename = null!;
        public int count;
        public List<object> data = new List<object>();
    }

    public sealed class BJSONParameters
    {
        /// <summary>
        /// Optimize the schema for Datasets (default = True)
        /// </summary>
        public bool UseOptimizedDatasetSchema = true;

        /// <summary>
        /// Serialize readonly properties (default = False)
        /// </summary>
        public bool ShowReadOnlyProperties = false;

        /// <summary>
        /// Use global types $types for more compact size when using a lot of classes (default = True)
        /// </summary>
        public bool UsingGlobalTypes = true;

        /// <summary>
        /// Use Unicode strings = T (faster), Use UTF8 strings = F (smaller) (default = True)
        /// </summary>
        public bool UseUnicodeStrings = true;

        /// <summary>
        /// Serialize Null values to the output (default = False)
        /// </summary>
        public bool SerializeNulls = false;

        /// <summary>
        /// Enable fastBinaryJSON extensions $types, $type, $map (default = True)
        /// </summary>
        public bool UseExtensions = true;

        /// <summary>
        /// Anonymous types have read only properties
        /// </summary>
        public bool EnableAnonymousTypes = false;

        /// <summary>
        /// Use the UTC date format (default = False)
        /// </summary>
        public bool UseUTCDateTime = false;

        /// <summary>
        /// Ignore attributes to check for (default : XmlIgnoreAttribute, NonSerialized)
        /// </summary>
        public List<Type> IgnoreAttributes = new List<Type> { typeof(System.Xml.Serialization.XmlIgnoreAttribute), typeof(NonSerializedAttribute) };

        /// <summary>
        /// If you have parametric and no default constructor for you classes (default = False)
        ///
        /// IMPORTANT NOTE : If True then all initial values within the class will be ignored and will be not set
        /// </summary>
        public bool ParametricConstructorOverride = false;

        /// <summary>
        /// Maximum depth the serializer will go to to avoid loops (default = 20 levels)
        /// </summary>
        public short SerializerMaxDepth = 20;

        /// <summary>
        /// Use typed arrays t[] into object = t[] not object[] (default = true)
        /// </summary>
        public bool UseTypedArrays = true;

        /// <summary>
        /// Backward compatible Typed array type name as UTF8 (default = false -> fast v1.5 unicode)
        /// </summary>
        public bool v1_4TypedArray = false;

        //public bool OptimizeSize = false;

        public void FixValues()
        {
            if (!UseExtensions) // disable conflicting params
                UsingGlobalTypes = false;

            if (EnableAnonymousTypes)
                ShowReadOnlyProperties = true;
        }

        internal BJSONParameters MakeCopy()
        {
            return new BJSONParameters
            {
                UseOptimizedDatasetSchema = UseOptimizedDatasetSchema,
                ShowReadOnlyProperties = ShowReadOnlyProperties,
                EnableAnonymousTypes = EnableAnonymousTypes,
                UsingGlobalTypes = UsingGlobalTypes,
                IgnoreAttributes = new List<Type>(IgnoreAttributes),
                UseUnicodeStrings = UseUnicodeStrings,
                SerializeNulls = SerializeNulls,
                ParametricConstructorOverride = ParametricConstructorOverride,
                SerializerMaxDepth = SerializerMaxDepth,
                UseTypedArrays = UseTypedArrays,
                UseExtensions = UseExtensions,
                UseUTCDateTime = UseUTCDateTime,
                v1_4TypedArray = v1_4TypedArray, //,
                //OptimizeSize = OptimizeSize
            };
        }
    }

    public static class BJSON
    {
        /// <summary>
        /// Globally set-able parameters for controlling the serializer
        /// </summary>
        public static BJSONParameters Parameters = new BJSONParameters();

        /// <summary>
        /// Parse a json and generate a Dictionary&lt;string,object&gt; or List&lt;object&gt; structure
        /// </summary>
        /// <param name="json"></param>
        /// <returns></returns>
        public static object? Parse(byte[] json)
        {
            return new BJsonParser(json, Parameters.UseUTCDateTime, Parameters.v1_4TypedArray).Decode();
        }

        /// <summary>
        /// Create a .net4 dynamic object from the binary json byte array
        /// </summary>
        /// <param name="json"></param>
        /// <returns></returns>
        public static dynamic ToDynamic(byte[] json)
        {
            return new DynamicJson(json);
        }

        /// <summary>
        /// Register custom type handlers for your own types not natively handled by fastBinaryJSON
        /// </summary>
        /// <param name="type"></param>
        /// <param name="serializer"></param>
        /// <param name="deserializer"></param>
        public static void RegisterCustomType(Type type, Reflection.Serialize serializer, Reflection.Deserialize deserializer)
        {
            Reflection.Instance.RegisterCustomType(type, serializer, deserializer);
        }

        /// <summary>
        /// Create a binary json representation for an object
        /// </summary>
        /// <param name="obj"></param>
        /// <returns></returns>
        public static byte[] ToBJSON(object obj)
        {
            return ToBJSON(obj, Parameters);
        }

        /// <summary>
        /// Create a binary json representation for an object with parameter override on this call
        /// </summary>
        /// <param name="obj"></param>
        /// <param name="param"></param>
        /// <returns></returns>
        public static byte[] ToBJSON(object obj, BJSONParameters param)
        {
            param.FixValues();
            param = param.MakeCopy();
            Type? t = null;
            if (obj == null)
                return new byte[] { TOKENS.NULL };
            if (obj.GetType().IsGenericType)
                t = Reflection.Instance.GetGenericTypeDefinition(obj.GetType()); // obj.GetType().GetGenericTypeDefinition();
            if (t == typeof(Dictionary<,>) || t == typeof(List<>))
                param.UsingGlobalTypes = false;
            // FEATURE : enable extensions when you can deserialize anon types
            if (param.EnableAnonymousTypes)
            {
                param.UseExtensions = false;
                param.UsingGlobalTypes = false;
            }

            return new BJSONSerializer(param).ConvertToBJSON(obj);
        }

        /// <summary>
        /// Fill a given object with the binary json represenation
        /// </summary>
        /// <param name="input"></param>
        /// <param name="json"></param>
        /// <returns></returns>
        public static object? FillObject(object input, byte[] json)
        {
            return new Deserializer(Parameters).FillObject(input, json);
        }

        /// <summary>
        /// Create a generic object from the json
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="json"></param>
        /// <returns></returns>
        public static T? ToObject<T>(byte[] json)
        {
            return new Deserializer(Parameters).ToObject<T>(json);
        }

        /// <summary>
        /// Create a generic object from the json with parameter override on this call
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="json"></param>
        /// <param name="param"></param>
        /// <returns></returns>
        public static T? ToObject<T>(byte[] json, BJSONParameters param)
        {
            return new Deserializer(param).ToObject<T>(json);
        }

        /// <summary>
        /// Create an object from the json
        /// </summary>
        /// <param name="json"></param>
        /// <returns></returns>
        public static object? ToObject(byte[] json)
        {
            return new Deserializer(Parameters).ToObject(json, null);
        }

        /// <summary>
        /// Create an object from the json with parameter override on this call
        /// </summary>
        /// <param name="json"></param>
        /// <param name="param"></param>
        /// <returns></returns>
        public static object? ToObject(byte[] json, BJSONParameters param)
        {
            param.FixValues();
            param = param.MakeCopy();
            return new Deserializer(param).ToObject(json, null);
        }

        /// <summary>
        /// Create a typed object from the json
        /// </summary>
        /// <param name="json"></param>
        /// <param name="type"></param>
        /// <returns></returns>
        public static object? ToObject(byte[] json, Type type)
        {
            return new Deserializer(Parameters).ToObject(json, type);
        }

        /// <summary>
        /// Clear the internal reflection cache so you can start from new (you will loose performance)
        /// </summary>
        public static void ClearReflectionCache()
        {
            Reflection.Instance.ClearReflectionCache();
        }

        /// <summary>
        /// Deep copy an object i.e. clone to a new object
        /// </summary>
        /// <param name="obj"></param>
        /// <returns></returns>
        public static object? DeepCopy(object obj)
        {
            return new Deserializer(Parameters).ToObject(ToBJSON(obj));
        }
    }

    internal class Deserializer
    {
        public Deserializer(BJSONParameters param)
        {
            _params = param;
            _params = param.MakeCopy();
        }

        private BJSONParameters _params;

        /*
         * $i numbering on read: entry n-1 is the object the writer numbered n. The writer numbers
         * every object it writes, in preorder, so this holds every object created from a document,
         * in creation order - by position, never by equality.
         *
         * Upstream kept a Dictionary<object, int> next to the reverse map and skipped any new object
         * that Equals an already numbered one. A new object has no members read yet, so a boxed struct
         * (all zeros) or a record with value equality could match an earlier one by accident, go
         * unnumbered, and shift every later $i onto the wrong object (defect 10).
         */
        private readonly List<object> _circular = new List<object>();

        /// <summary>
        /// Read typed ToObject calls with <see cref="TypedReader"/> instead of the two-step path.
        /// </summary>
        /// <remarks>
        /// On by default since ReaderEquivalenceTests proved it matches the two-step path on every
        /// golden case, the benchmark corpus under six parameter sets and seeded damaged input. Parse,
        /// ToDynamic, untyped ToObject, DeepCopy and FillObject never take this branch. Off only in
        /// tests, to run the two-step path as the reference.
        /// </remarks>
        internal bool OneStep { get; set; } = true;

        /// <summary>
        /// How many objects TypedReader handed back to the two-step path. Diagnostic only - lets the
        /// tests prove the reader did the work instead of quietly falling back every time.
        /// </summary>
        internal int OneStepFallbacks { get; set; }

        /// <summary>
        /// How many members TypedReader set without boxing. Diagnostic only, like OneStepFallbacks -
        /// lets the tests prove the typed path ran rather than everything taking the boxing setter.
        /// </summary>
        internal int TypedSets { get; set; }

        /// <summary>
        /// How many $type values TypedReader resolved without allocating the name. Diagnostic only;
        /// always 0 on netstandard2.0, which keeps the allocating path.
        /// </summary>
        internal int TypesResolvedInPlace { get; set; }

        /// <summary>
        /// How many $type values TypedReader took from the previous one by comparing bytes. Diagnostic only.
        /// </summary>
        internal int TypesRepeated { get; set; }

        public T? ToObject<T>(byte[] json)
        {
            return (T?)ToObject(json, typeof(T));
        }

        public object? ToObject(byte[] json)
        {
            return ToObject(json, null);
        }

        public object? ToObject(byte[] json, Type? type)
        {
            //_params.FixValues();
            Type? t = null;
            if (type != null && type.IsGenericType)
                t = Reflection.Instance.GetGenericTypeDefinition(type); // type.GetGenericTypeDefinition();
            _globalTypes = _params.UsingGlobalTypes;
            if (t == typeof(Dictionary<,>) || t == typeof(List<>))
                _globalTypes = false;

            if (OneStep && type != null && TypedReader.TryRead(this, json, type, t, out object? read))
                return read;

            var o = new BJsonParser(json, _params.UseUTCDateTime, _params.v1_4TypedArray).Decode();
            if (type?.IsEnum == true)
                return CreateEnum(type!, o!);
            if (type != null && type == typeof(DataSet))
                return CreateDataset(o as Dictionary<string, object>, null);

            if (type != null && type == typeof(DataTable))
                return CreateDataTable(o as Dictionary<string, object>, null);
            if (o is TypedArray)
            {
                return ParseTypedArray(new Dictionary<string, object>(), o);
            }
            if (o is IDictionary)
            {
                if (type != null && t == typeof(Dictionary<,>)) // deserialize a dictionary
                    return RootDictionary(o, type);
                else // deserialize an object
                    return ParseDictionary(o as Dictionary<string, object>, null, type, null);
            }

            if (o is List<object>)
            {
                if (type != null && t == typeof(Dictionary<,>)) // kv format
                    return RootDictionary(o, type);

                if (type != null && t == typeof(List<>)) // deserialize to generic list
                    return RootList(o, type);

                if (type == typeof(Hashtable))
                    return RootHashTable((List<object>)o);
                else if (type == null)
                {
                    List<object> l = (List<object>)o;
                    if (l.Count > 0 && l[0].GetType() == typeof(Dictionary<string, object>))
                    {
                        Dictionary<string, object> globals = new Dictionary<string, object>();
                        List<object> op = new List<object>();
                        // try to get $types
                        foreach (var i in l)
                            op.Add(ParseDictionary((Dictionary<string, object>)i, globals, null, null)!);
                        return op;
                    }
                    return l.ToArray();
                }
            }
            else if (type != null && o!.GetType() != type)
                return ChangeType(o, type);

            return o;
        }

        private object? ChangeType(object? o, Type type)
        {
            if (Reflection.Instance.IsTypeRegistered(type))
                return Reflection.Instance.CreateCustom((string)o!, type);
            else
                return o;
        }

        public object? FillObject(object input, byte[] json)
        {
            _params.FixValues();
            Dictionary<string, object>? ht = new BJsonParser(json, _params.UseUTCDateTime, _params.v1_4TypedArray).Decode() as Dictionary<string, object>;
            if (ht == null)
                return null;
            return ParseDictionary(ht, null, input.GetType(), input);
        }

        private object RootHashTable(List<object> o)
        {
            Hashtable h = new Hashtable();

            foreach (Dictionary<string, object> values in o)
            {
                object? key = values["k"];
                object? val = values["v"];
                if (key is Dictionary<string, object>)
                    key = ParseDictionary((Dictionary<string, object>)key!, null, typeof(object), null);

                if (val is Dictionary<string, object>)
                    val = ParseDictionary((Dictionary<string, object>)val!, null, typeof(object), null);

                h.Add(key!, val);
            }

            return h;
        }

        private object RootList(object parse, Type? type)
        {
            Type[] gtypes = Reflection.Instance.GetGenericArguments(type!); // type.GetGenericArguments();
            IList o = (IList)Reflection.Instance.FastCreateList(type!, ((IList)parse).Count);
            Dictionary<string, object> globals = new Dictionary<string, object>();

            foreach (var k in (IList)parse)
            {
                _globalTypes = false;
                object? v = k;
                if (k is Dictionary<string, object>)
                    v = ParseDictionary(k as Dictionary<string, object>, globals, gtypes[0], null);
                else
                    v = k;

                o.Add(v);
            }
            return o;
        }

        private object? RootDictionary(object parse, Type type)
        {
            Type[] gtypes = Reflection.Instance.GetGenericArguments(type);
            Type? t1 = null;
            Type? t2 = null;
            if (gtypes != null)
            {
                t1 = gtypes[0];
                t2 = gtypes[1];
            }
            var arraytype = t2!.GetElementType();

            if (parse is Dictionary<string, object>)
            {
                IDictionary o = (IDictionary)Reflection.Instance.FastCreateInstance(type);

                foreach (var kv in (Dictionary<string, object>)parse)
                {
                    _globalTypes = false;
                    object? v;
                    object k = kv.Key;
                    if (t2!.Name.StartsWith("Dictionary")) // deserialize a dictionary
                        v = RootDictionary(kv.Value, t2!);
                    else if (kv.Value is Dictionary<string, object>)
                        v = ParseDictionary(kv.Value as Dictionary<string, object>, null, t2, null);
                    else if (t2 == typeof(byte[]))
                        v = kv.Value;
                    else if (gtypes != null && t2.IsArray)
                        v = CreateArray((List<object>)kv.Value, t2, arraytype, null);
                    else if (kv.Value is IList)
                        v = CreateGenericList((List<object>)kv.Value, t2, t1, null);
                    else
                        v = kv.Value;

                    o.Add(k, v);
                }

                return o;
            }
            if (parse is List<object>)
                return CreateDictionary(parse as List<object>, type, gtypes, null);

            return null;
        }

        private bool _globalTypes = false;

        #region Shared with TypedReader

        /*
         * Pieces of ParseDictionary that TypedReader has to perform identically - extracted rather
         * than duplicated, so the two paths cannot drift apart.
         */

        internal BJSONParameters Parameters => _params;

        internal object CreateInstance(Type type)
        {
            if (_params.ParametricConstructorOverride)
#if NET10_0_OR_GREATER
                // FormatterServices is obsolete (SYSLIB0050) on modern .NET. RuntimeHelpers is
                // its documented replacement and behaves identically; it does not exist on
                // netstandard2.0, so this is the one genuine TFM conditional in the library.
                return System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(type);
#else
                return System.Runtime.Serialization.FormatterServices.GetUninitializedObject(type);
#endif

            return Reflection.Instance.FastCreateInstance(type);
        }

        /// <summary>
        /// Numbers an instance for $i references, in creation order - unless an equal instance is
        /// already numbered.
        /// </summary>
        internal int RegisterCircular(object o)
        {
            _circular.Add(o);
            return _circular.Count;
        }

        /// <summary>
        /// Points $i <paramref name="id"/> at a struct's finished box.
        /// </summary>
        /// <remarks>
        /// A struct's setter returns a new box per member, so the box numbered at creation never sees
        /// a member value. A later $i to the struct - the writer emits one for a second, equal struct -
        /// resolved to that empty box and restored all zeros (defect 11).
        /// </remarks>
        internal void UpdateCircular(int id, object o)
        {
            _circular[id - 1] = o;
        }

        internal int CircularCount => _circular.Count;

        /// <summary>
        /// Forgets every instance numbered after <paramref name="count"/>, so a subtree TypedReader
        /// abandons for the two-step path is numbered again, in the same order, when that path runs.
        /// </summary>
        internal void UndoCircular(int count)
        {
            _circular.RemoveRange(count, _circular.Count - count);
        }

        // An id never numbered resolves to null, as the dictionary lookup it replaces did.
        internal object? ResolveCircular(object id)
        {
            int index = (int)id - 1;
            return index >= 0 && index < _circular.Count ? _circular[index] : null;
        }

        internal Type? ResolveType(object tn, Dictionary<string, object>? globaltypes)
        {
            if (globaltypes != null && globaltypes.TryGetValue((string)tn, out object? entry))
                return ResolveGlobalType(entry);

            return Reflection.Instance.GetTypeFromCache((string)tn, true);
        }

        /*
         * An object's $type is usually an index into $types, and resolving the entry it names hashed
         * the whole assembly-qualified name again for every object. The last entry is kept with its
         * type, which covers a collection of one type. Compared by instance: the same entry is the
         * same name, so the answer is GetTypeFromCache's by construction, and an entry that throws
         * (denylisted, not a string) is never kept, so it throws every time as before.
         */
        private object? _lastTypeEntry;
        private Type? _lastType;

        internal Type? ResolveGlobalType(object entry)
        {
            if (ReferenceEquals(entry, _lastTypeEntry))
                return _lastType;

            Type? type = Reflection.Instance.GetTypeFromCache((string)entry, true);
            _lastTypeEntry = entry;
            _lastType = type;
            return type;
        }

        #endregion

        internal object? ParseDictionary(Dictionary<string, object>? d, Dictionary<string, object>? globaltypes, Type? type, object? input)
        {
            object? tn = "";
            if (type == typeof(NameValueCollection))
                return CreateNV(d!);
            if (type == typeof(StringDictionary))
                return CreateSD(d!);

            if (d!.TryGetValue("$i", out tn))
                return ResolveCircular(tn!);

            if (d!.TryGetValue("$types", out tn))
            {
                _globalTypes = true;
                if (globaltypes == null)
                    globaltypes = new Dictionary<string, object>();
                foreach (var kv in (Dictionary<string, object>)tn)
                {
                    globaltypes.Add((string)kv.Key, kv.Value);
                }
            }

            if (globaltypes != null)
                _globalTypes = true;

            bool found = d!.TryGetValue("$type", out tn);
            if (!found && type == typeof(System.Object))
            {
                return d; // CreateDataset(d, globaltypes);
            }
            // _globalTypes is always true here when globaltypes is non-null - set just above.
            if (found)
                type = ResolveType(tn!, _globalTypes ? globaltypes : null);

            if (type == null)
                throw new Exception("Cannot determine type");

            string typename = type.FullName!;
            object? o = input ?? CreateInstance(type);
            int id = RegisterCircular(o);

            WireNameMap props = Reflection.Instance.GetWireNameMap(type, typename, _params.ShowReadOnlyProperties); //, Reflection.Instance.IsTypeRegistered(type));
            foreach (var kv in d!)
            {
                var v = kv.Value;
                myPropInfo? pi = props.Find(kv.Key);
                if (pi == null)
                    continue;
                if (pi.CanWrite && v != null)
                    o = pi.setter!(o!, ConvertValue(pi, v, globaltypes)!);
            }

            if (type.IsValueType)
                UpdateCircular(id, o!);
            return o;
        }

        /// <summary>
        /// Converts a value as the parser produced it into what the member's setter takes.
        /// </summary>
        /// <remarks>
        /// Shared by the two-step path (ParseDictionary) and the one-step TypedReader, so both apply
        /// exactly the same conversion rules - the reader only decides which values it can read
        /// directly and hands every other one here, already materialised by the parser.
        /// </remarks>
        internal object? ConvertValue(myPropInfo pi, object v, Dictionary<string, object>? globaltypes)
        {
            if (v is TypedArray)
                return ParseTypedArray(globaltypes, v);

            switch (pi.Type)
            {
                case myPropInfoType.DataSet:
                    return CreateDataset((Dictionary<string, object>)v, globaltypes);
                case myPropInfoType.DataTable:
                    return CreateDataTable((Dictionary<string, object>)v, globaltypes);
                case myPropInfoType.Custom:
                    return Reflection.Instance.CreateCustom((string)v, pi.pt);
                case myPropInfoType.Enum:
                    return CreateEnum(pi.pt, v);
                case myPropInfoType.SByte:
                    return CreateSByte(v);
                case myPropInfoType.StringKeyDictionary:
                    return CreateStringKeyDictionary((Dictionary<string, object>)v, pi.pt, pi.GenericTypes, globaltypes);
                case myPropInfoType.Hashtable:
                case myPropInfoType.Dictionary:
                    return CreateDictionary((List<object>)v, pi.pt, pi.GenericTypes, globaltypes);
                case myPropInfoType.NameValue:
                    return CreateNV((Dictionary<string, object>)v);
                case myPropInfoType.StringDictionary:
                    return CreateSD((Dictionary<string, object>)v);
                case myPropInfoType.Array:
                    return CreateArray((List<object>)v, pi.pt, pi.bt, globaltypes);
            }

            if (pi.IsGenericType && !pi.IsValueType)
                return CreateGenericList((List<object>)v, pi.pt, pi.bt, globaltypes);

            if ((pi.IsClass || pi.IsStruct || pi.IsInterface) && v is Dictionary<string, object>)
            {
                var oo = (Dictionary<string, object>)v;
                if (oo.ContainsKey("$schema"))
                    return CreateDataset(oo, globaltypes);

                /*
                 * null, not the caller's `input`: that is the ROOT instance FillObject was given, and
                 * passing it down made every nested member be filled into the root - its setters then
                 * threw InvalidCastException. Nested members get a new instance, the same as ToObject
                 * gives them.
                 */
                return ParseDictionary(oo, globaltypes, pi.pt, null);
            }

            if (v is List<object>)
                return CreateArray((List<object>)v, pi.pt, typeof(object), globaltypes);

            return v;
        }

        /// <summary>
        /// Restores an sbyte from either token, so streams written before TOKENS.SBYTE still load.
        /// </summary>
        /// <remarks>
        /// A pre-fix writer emitted TOKENS.BYTE for an sbyte, so the parser hands back a byte and
        /// the value has lost its sign: -42 was written as 214. Reinterpreting the bits is what
        /// recovers it. Nothing else can - the old bytes do not record that the value was signed,
        /// which is why the untyped read path cannot be repaired the same way.
        /// </remarks>
        private static sbyte CreateSByte(object value)
        {
            return value is sbyte signed ? signed : unchecked((sbyte)(byte)value);
        }

        private object ParseTypedArray(Dictionary<string, object>? globaltypes, object v)
        {
            object oset;
            var ta = (TypedArray)v;
            var t = Reflection.Instance.GetTypeFromCache(ta.typename, true);
            IList a = Array.CreateInstance(t!, ta.count);
            int i = 0;
            foreach (var dd in ta.data)
            {
                object? oo = null;
                if (dd == null)
                    oo = null;
                else if (dd is TypedArray)
                    oo = ParseTypedArray(globaltypes, dd);
                else if (dd is Dictionary<string, object>)
                    oo = ParseDictionary((Dictionary<string, object>)dd!, globaltypes, t, null);
                else if (dd is List<object>)
                    oo = CreateArray((List<object>)dd!, t!, t!.GetElementType(), globaltypes);
                else
                    oo = dd;
                a[i++] = oo;
            }
            oset = a;
            return oset;
        }

        private StringDictionary CreateSD(Dictionary<string, object> d)
        {
            StringDictionary nv = new StringDictionary();

            foreach (var o in d)
                nv.Add(o.Key, (string)o.Value);

            return nv;
        }

        private NameValueCollection CreateNV(Dictionary<string, object> d)
        {
            NameValueCollection nv = new NameValueCollection();

            foreach (var o in d)
                nv.Add(o.Key, (string)o.Value);

            return nv;
        }

        private object CreateEnum(Type pt, object v)
        {
            // FEATURE : optimize create enum
            return Enum.Parse(pt, v.ToString()!);
        }

        private object CreateArray(List<object> data, Type pt, Type? bt, Dictionary<string, object>? globalTypes)
        {
            if (bt == null)
                bt = typeof(object);

            Array col = Array.CreateInstance(bt, data.Count);
            var arraytype = bt.GetElementType();
            // create an array of objects
            for (int i = 0; i < data.Count; i++) // each (object ob in data)
            {
                object ob = data[i];
                if (ob == null)
                {
                    continue;
                }
                if (ob is IDictionary)
                    col.SetValue(ParseDictionary((Dictionary<string, object>)ob, globalTypes, bt, null), i);
                else if (ob is ICollection)
                    col.SetValue(CreateArray((List<object>)ob, bt, arraytype, globalTypes), i);
                else
                    col.SetValue(ob, i);
            }

            return col;
        }

        private object CreateGenericList(List<object> data, Type pt, Type? bt, Dictionary<string, object>? globalTypes)
        {
            if (pt != typeof(object))
            {
                IList col = (IList)Reflection.Instance.FastCreateList(pt, data.Count);
                // create an array of objects
                foreach (object ob in data)
                {
                    if (ob is IDictionary)
                        col.Add(ParseDictionary((Dictionary<string, object>)ob, globalTypes, bt, null));
                    else if (ob is List<object>)
                    {
                        if (bt!.IsGenericType)
                            col.Add((List<object>)ob);
                        else
                            col.Add(((List<object>)ob).ToArray());
                    }
                    else if (ob is TypedArray)
                        col.Add(((TypedArray)ob).data.ToArray());
                    else
                        col.Add(ob);
                }
                return col;
            }
            return data;
        }

        private object CreateStringKeyDictionary(Dictionary<string, object> reader, Type pt, Type[]? types, Dictionary<string, object>? globalTypes)
        {
            var col = (IDictionary)Reflection.Instance.FastCreateInstance(pt);
            Type? arraytype = null;
            Type? t2 = null;
            if (types != null)
                t2 = types[1];

            Type? generictype = null;
            var ga = Reflection.Instance.GetGenericArguments(t2!);
            if (ga.Length > 0)
                generictype = ga[0];
            arraytype = t2!.GetElementType();

            foreach (KeyValuePair<string, object> values in reader)
            {
                var key = values.Key;
                object? val = null;

                if (values.Value is Dictionary<string, object>)
                    val = ParseDictionary((Dictionary<string, object>)values.Value!, globalTypes, t2, null);
                else if (types != null && t2.IsArray)
                {
                    if (values.Value is Array)
                        val = values.Value;
                    else
                        val = CreateArray((List<object>)values.Value, t2, arraytype, globalTypes);
                }
                else if (values.Value is IList)
                    val = CreateGenericList((List<object>)values.Value, t2, generictype, globalTypes);
                else
                    val = values.Value;

                col.Add(key, val);
            }

            return col;
        }

        private object CreateDictionary(List<object>? reader, Type pt, Type[]? types, Dictionary<string, object>? globalTypes)
        {
            IDictionary col = (IDictionary)Reflection.Instance.FastCreateInstance(pt);
            Type? t1 = null;
            Type? t2 = null;
            if (types != null)
            {
                t1 = types[0];
                t2 = types[1];
            }

            foreach (Dictionary<string, object> values in reader!)
            {
                object? key = values["k"];
                object? val = values["v"];

                if (key is Dictionary<string, object>)
                    key = ParseDictionary((Dictionary<string, object>)key!, globalTypes, t1, null);

                if (typeof(IDictionary).IsAssignableFrom(t2))
                    val = RootDictionary(val!, t2!);
                else if (val is Dictionary<string, object>)
                    val = ParseDictionary((Dictionary<string, object>)val!, globalTypes, t2, null);

                col.Add(key!, val);
            }

            return col;
        }

        private DataSet CreateDataset(Dictionary<string, object>? reader, Dictionary<string, object>? globalTypes)
        {
            DataSet ds = new DataSet();
            ds.EnforceConstraints = false;
            ds.BeginInit();

            // read dataset schema here
            var schema = reader!["$schema"];

            if (schema is string)
            {
                TextReader tr = new StringReader((string)schema);
                ds.ReadXmlSchema(tr);
            }
            else
            {
                DatasetSchema ms = (DatasetSchema)ParseDictionary((Dictionary<string, object>)schema!, globalTypes, typeof(DatasetSchema), null)!;
                ds.DataSetName = ms.Name!;
                for (int i = 0; i < ms.Info!.Count; i += 3)
                {
                    if (!ds.Tables.Contains(ms.Info[i]))
                        ds.Tables.Add(ms.Info[i]);
                    ds.Tables[ms.Info![i]]!.Columns.Add(ms.Info[i + 1], Type.GetType(ms.Info[i + 2])!);
                }
            }

            foreach (KeyValuePair<string, object> pair in reader)
            {
                if (pair.Key == "$type" || pair.Key == "$schema")
                    continue;

                List<object> rows = (List<object>)pair.Value;
                if (rows == null)
                    continue;

                DataTable dt = ds.Tables[pair.Key]!;
                ReadDataTable(rows, dt);
            }

            ds.EndInit();

            return ds;
        }

        private void ReadDataTable(List<object> rows, DataTable dt)
        {
            dt.BeginInit();
            dt.BeginLoadData();

            foreach (List<object> row in rows)
            {
                object[] v = new object[row.Count];
                row.CopyTo(v, 0);
                dt.Rows.Add(v);
            }

            dt.EndLoadData();
            dt.EndInit();
        }

        DataTable CreateDataTable(Dictionary<string, object>? reader, Dictionary<string, object>? globalTypes)
        {
            var dt = new DataTable();

            // read dataset schema here
            var schema = reader!["$schema"];

            if (schema is string)
            {
                TextReader tr = new StringReader((string)schema);
                dt.ReadXmlSchema(tr);
            }
            else
            {
                var ms = (DatasetSchema)this.ParseDictionary((Dictionary<string, object>)schema!, globalTypes, typeof(DatasetSchema), null)!;
                dt.TableName = ms.Info![0];
                for (int i = 0; i < ms.Info.Count; i += 3)
                {
                    dt.Columns.Add(ms.Info![i + 1], Type.GetType(ms.Info[i + 2])!);
                }
            }

            foreach (var pair in reader)
            {
                if (pair.Key == "$type" || pair.Key == "$schema")
                    continue;

                var rows = (List<object>)pair.Value;
                if (rows == null)
                    continue;

                if (!dt.TableName.Equals(pair.Key, StringComparison.InvariantCultureIgnoreCase))
                    continue;

                ReadDataTable(rows, dt);
            }

            return dt;
        }
    }
}
