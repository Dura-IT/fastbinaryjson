using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Data;
using System.IO;
using System.Xml;
using DuraIT.FastBinaryJson.Internal;

namespace DuraIT.FastBinaryJson
{
#pragma warning disable CA1720 // Each token is named for the type it denotes on the wire, which is the point of the name.
    /// <summary>
    /// The byte tokens that tag every value in the binary JSON format. Exposed for code that reads or
    /// writes the wire format directly; each token is named for the type it carries.
    /// </summary>
    public static class Tokens
    {
        /// <summary>
        /// Opens an object.
        /// </summary>
        public const byte DocStart = 1;

        /// <summary>
        /// Closes an object.
        /// </summary>
        public const byte DocEnd = 2;

        /// <summary>
        /// Opens an array.
        /// </summary>
        public const byte ArrayStart = 3;

        /// <summary>
        /// Closes an array.
        /// </summary>
        public const byte ArrayEnd = 4;

        /// <summary>
        /// Separates a member name from its value.
        /// </summary>
        public const byte Colon = 5;

        /// <summary>
        /// Separates members or array elements.
        /// </summary>
        public const byte Comma = 6;

        /// <summary>
        /// A member name, UTF-8 encoded, with a one-byte length.
        /// </summary>
        public const byte Name = 7;

        /// <summary>
        /// A UTF-8 string with a four-byte length.
        /// </summary>
        public const byte Utf8String = 8;

        /// <summary>
        /// An unsigned 8-bit integer.
        /// </summary>
        public const byte Byte = 9;

        /// <summary>
        /// A signed 32-bit integer.
        /// </summary>
        public const byte Int32 = 10;

        /// <summary>
        /// An unsigned 32-bit integer.
        /// </summary>
        public const byte UInt32 = 11;

        /// <summary>
        /// A signed 64-bit integer.
        /// </summary>
        public const byte Int64 = 12;

        /// <summary>
        /// An unsigned 64-bit integer.
        /// </summary>
        public const byte UInt64 = 13;

        /// <summary>
        /// A signed 16-bit integer.
        /// </summary>
        public const byte Int16 = 14;

        /// <summary>
        /// An unsigned 16-bit integer.
        /// </summary>
        public const byte UInt16 = 15;

        /// <summary>
        /// A <see cref="System.DateTime"/>.
        /// </summary>
        public const byte DateTime = 16;

        /// <summary>
        /// A <see cref="System.Guid"/>, as its 16 raw bytes.
        /// </summary>
        public const byte Guid = 17;

        /// <summary>
        /// A 64-bit floating point number.
        /// </summary>
        public const byte Double = 18;

        /// <summary>
        /// A 32-bit floating point number.
        /// </summary>
        public const byte Single = 19;

        /// <summary>
        /// A <see cref="System.Decimal"/>.
        /// </summary>
        public const byte Decimal = 20;

        /// <summary>
        /// A <see cref="System.Char"/>, written as a 16-bit integer.
        /// </summary>
        public const byte Char = 21;

        /// <summary>
        /// A byte array with a four-byte length.
        /// </summary>
        public const byte ByteArray = 22;

        /// <summary>
        /// A null value.
        /// </summary>
        public const byte Null = 23;

        /// <summary>
        /// The boolean value true.
        /// </summary>
        public const byte True = 24;

        /// <summary>
        /// The boolean value false.
        /// </summary>
        public const byte False = 25;

        /// <summary>
        /// A UTF-16 string with a four-byte length.
        /// </summary>
        public const byte Utf16String = 26;

        /// <summary>
        /// A <see cref="System.DateTimeOffset"/>.
        /// </summary>
        public const byte DateTimeOffset = 27;

        /// <summary>
        /// Opens a typed array. The element type name and the element count follow.
        /// </summary>
        public const byte TypedArray = 28;

        /// <summary>
        /// A four-byte offset to the $types table written at the end of the stream.
        /// </summary>
        public const byte TypesPointer = 29;

        /// <summary>
        /// A <see cref="System.TimeSpan"/>, as its ticks.
        /// </summary>
        public const byte TimeSpan = 30;

        /// <summary>
        /// Opens a typed array whose element type name is 256 encoded bytes or longer.
        /// </summary>
        public const byte TypedArrayLong = 31;

        /// <summary>
        /// A member name, UTF-16 encoded, with a one-byte length.
        /// </summary>
        public const byte NameUtf16 = 32;

        /*
         * Added by this fork. Upstream stops at 32, so 33 onwards were free.
         *
         * SBYTE exists because WriteSByte emitted BYTE after casting, which left sbyte and byte
         * indistinguishable on the wire with no way for a reader to recover the sign. A stream
         * written before this token still reads: BYTE assigned to an sbyte property is
         * reinterpreted. The reverse does not hold - upstream rejects this token as unknown.
         */
        /// <summary>
        /// A signed 8-bit integer.
        /// </summary>
        public const byte SByte = 33;

        /*
         * The long form of Tokens.Name and Tokens.NameUtf16, carrying a four-byte length instead of one, which is
         * how WriteString has always carried its own. WriteName put the encoded length in a single
         * byte and wrote `b.Length % 256` bytes, so every name of 256 encoded bytes or more was
         * silently truncated - 128 characters at the default UseUnicodeStrings = true.
         *
         * Written only from 256 encoded bytes onwards, so every name that survived before is still
         * encoded exactly as it was. Only input that was already being corrupted produces these
         * tokens, which is why upstream not accepting them costs nothing.
         */
        /// <summary>
        /// A member name, UTF-8 encoded, with a four-byte length. Written from 256 encoded bytes onwards.
        /// </summary>
        public const byte NameLong = 34;

        /// <summary>
        /// A member name, UTF-16 encoded, with a four-byte length. Written from 256 encoded bytes onwards.
        /// </summary>
        public const byte NameUtf16Long = 35;
    }
#pragma warning restore CA1720

    /// <summary>
    /// A typed array as it appears on the wire: the declared element type, the declared element count,
    /// and the elements. <see cref="Bjson.Parse(byte[])"/> returns one for a typed array it is not
    /// asked to convert.
    /// </summary>
    public class TypedArray
    {
        /// <summary>
        /// The assembly-qualified name of the element type.
        /// </summary>
        public string TypeName { get; set; } = null!;

        /// <summary>
        /// The element count declared in the payload.
        /// </summary>
        public int Count { get; set; }
        private readonly List<object> _data = new List<object>();

        /// <summary>
        /// The elements, in order.
        /// </summary>
        public IList<object> Data => _data;

        // The parser appends and the readers copy out through the concrete list, not the interface.
        internal List<object> DataList => _data;
    }

    /// <summary>
    /// Settings for serializing and deserializing. Pass an instance to a single call, or set
    /// <see cref="Bjson.Parameters"/> to change the default for every call that does not.
    /// </summary>
    public sealed class BjsonParameters
    {
        /// <summary>
        /// Optimize the schema for Datasets (default = True)
        /// </summary>
        public bool UseOptimizedDatasetSchema { get; set; } = true;

        /// <summary>
        /// Serialize readonly properties (default = False)
        /// </summary>
        public bool ShowReadOnlyProperties { get; set; }

        /// <summary>
        /// Use global types $types for more compact size when using a lot of classes (default = True)
        /// </summary>
        public bool UsingGlobalTypes { get; set; } = true;

        /// <summary>
        /// Use Unicode strings = T (faster), Use UTF8 strings = F (smaller) (default = True)
        /// </summary>
        public bool UseUnicodeStrings { get; set; } = true;

        /// <summary>
        /// Serialize Null values to the output (default = False)
        /// </summary>
        public bool SerializeNulls { get; set; }

        /// <summary>
        /// Enable fastBinaryJSON extensions $types, $type, $map (default = True)
        /// </summary>
        public bool UseExtensions { get; set; } = true;

        /// <summary>
        /// Anonymous types have read only properties
        /// </summary>
        public bool EnableAnonymousTypes { get; set; }

        /// <summary>
        /// Use the UTC date format (default = False)
        /// </summary>
        public bool UseUtcDateTime { get; set; }

        /// <summary>
        /// Ignore attributes to check for (default : XmlIgnoreAttribute, NonSerialized)
        /// </summary>
        public IList<Type> IgnoreAttributes { get; }

        /// <summary>
        /// Creates parameters with the default settings.
        /// </summary>
        public BjsonParameters()
        {
            IgnoreAttributes = new List<Type> { typeof(System.Xml.Serialization.XmlIgnoreAttribute), typeof(NonSerializedAttribute) };
        }

        // For MakeCopy: takes the already copied ignore list, instead of building the default one just to
        // clear it and refill it on every call.
        private BjsonParameters(IList<Type> ignoreAttributes)
        {
            IgnoreAttributes = ignoreAttributes;
        }

        /// <summary>
        /// If you have parametric and no default constructor for you classes (default = False)
        ///
        /// IMPORTANT NOTE : If True then all initial values within the class will be ignored and will be not set
        /// </summary>
        public bool ParametricConstructorOverride { get; set; }

        /// <summary>
        /// Maximum depth the serializer will go to to avoid loops (default = 20 levels)
        /// </summary>
        public short SerializerMaxDepth { get; set; } = 20;

        /// <summary>
        /// Use typed arrays t[] into object = t[] not object[] (default = true)
        /// </summary>
        public bool UseTypedArrays { get; set; } = true;

        /// <summary>
        /// Backward compatible Typed array type name as UTF8 (default = false -> fast v1.5 unicode)
        /// </summary>
        public bool UseV14TypedArray { get; set; }

        /// <summary>
        /// Resolves settings that conflict: switching extensions off also switches global types off, and
        /// anonymous types imply that read-only properties are written.
        /// </summary>
        public void FixValues()
        {
            if (!UseExtensions) // disable conflicting params
                UsingGlobalTypes = false;

            if (EnableAnonymousTypes)
                ShowReadOnlyProperties = true;
        }

        internal BjsonParameters MakeCopy()
        {
            BjsonParameters copy = new BjsonParameters(new List<Type>(IgnoreAttributes))
            {
                UseOptimizedDatasetSchema = UseOptimizedDatasetSchema,
                ShowReadOnlyProperties = ShowReadOnlyProperties,
                EnableAnonymousTypes = EnableAnonymousTypes,
                UsingGlobalTypes = UsingGlobalTypes,
                UseUnicodeStrings = UseUnicodeStrings,
                SerializeNulls = SerializeNulls,
                ParametricConstructorOverride = ParametricConstructorOverride,
                SerializerMaxDepth = SerializerMaxDepth,
                UseTypedArrays = UseTypedArrays,
                UseExtensions = UseExtensions,
                UseUtcDateTime = UseUtcDateTime,
                UseV14TypedArray = UseV14TypedArray,
            };

            return copy;
        }
    }

    /// <summary>
    /// Serializes objects to the binary JSON format and reads them back.
    /// </summary>
    public static class Bjson
    {
        /// <summary>
        /// Globally set-able parameters for controlling the serializer. Calls that do not pass their own
        /// <see cref="BjsonParameters"/> use these.
        /// </summary>
        public static BjsonParameters Parameters { get; set; } = new BjsonParameters();

        /// <summary>
        /// Parses a payload without needing the target type.
        /// </summary>
        /// <param name="json">The binary JSON bytes.</param>
        /// <returns>A Dictionary&lt;string, object&gt;, a List&lt;object&gt;, a <see cref="TypedArray"/> or a scalar; null for a null payload.</returns>
        /// <exception cref="ArgumentNullException">If <paramref name="json"/> is null.</exception>
        /// <exception cref="BjsonException">If the payload holds an unrecognized token. Other corrupt or truncated bytes can throw other exception types.</exception>
        /// <exception cref="ArgumentOutOfRangeException">If a length in the payload runs past its end.</exception>
        public static object? Parse(byte[] json)
        {
            Guard.NotNull(json, nameof(json));
            return new BjsonParser(json, Parameters.UseUtcDateTime, Parameters.UseV14TypedArray).Decode();
        }

        /// <summary>
        /// Parses a payload into a dynamic object whose members are the JSON keys. A member is looked up as
        /// written first, then ignoring case.
        /// </summary>
        /// <param name="json">The binary JSON bytes.</param>
        /// <returns>A dynamic view over the parsed payload.</returns>
        /// <exception cref="ArgumentNullException">If <paramref name="json"/> is null.</exception>
        public static dynamic ToDynamic(byte[] json)
        {
            Guard.NotNull(json, nameof(json));
            return new DynamicJson(json);
        }

        /// <summary>
        /// Registers handlers that store a type your own way, for types the serializer does not handle natively.
        /// A registration also applies to subclasses of <paramref name="type"/>.
        /// </summary>
        /// <remarks>
        /// Registrations are process-wide.
        /// </remarks>
        /// <param name="type">The type to handle.</param>
        /// <param name="serializer">Turns a value into the string stored in its place.</param>
        /// <param name="deserializer">Turns the stored string back into a value.</param>
        /// <exception cref="ArgumentNullException">If <paramref name="type"/> or <paramref name="serializer"/> or <paramref name="deserializer"/> is null.</exception>
        public static void RegisterCustomType(Type type, CustomTypeSerializer serializer, CustomTypeDeserializer deserializer)
        {
            Guard.NotNull(type, nameof(type));
            Guard.NotNull(serializer, nameof(serializer));
            Guard.NotNull(deserializer, nameof(deserializer));
            TypeReflector.Instance.RegisterCustomType(type, serializer, deserializer);
        }

        /// <summary>
        /// Serializes an object using <see cref="Parameters"/>.
        /// </summary>
        /// <param name="obj">The object to write; null is written as the null token.</param>
        /// <returns>The binary JSON bytes.</returns>
        /// <exception cref="BjsonException">If the object graph is deeper than <see cref="BjsonParameters.SerializerMaxDepth"/>.</exception>
        public static byte[] ToBjson(object? obj)
        {
            return ToBjson(obj, Parameters);
        }

        /// <summary>
        /// Serializes an object using the given parameters for this call only.
        /// </summary>
        /// <param name="obj">The object to write; null is written as the null token.</param>
        /// <param name="param">The settings for this call.</param>
        /// <returns>The binary JSON bytes.</returns>
        /// <exception cref="ArgumentNullException">If <paramref name="param"/> is null.</exception>
        /// <exception cref="BjsonException">If the object graph is deeper than <see cref="BjsonParameters.SerializerMaxDepth"/>.</exception>
        public static byte[] ToBjson(object? obj, BjsonParameters param)
        {
            Guard.NotNull(param, nameof(param));
            param = param.MakeCopy();
            param.FixValues();
            Type? t = null;
            if (obj == null)
                return new[] { Tokens.Null };
            if (obj.GetType().IsGenericType)
                t = TypeReflector.Instance.GetGenericTypeDefinition(obj.GetType());
            if (t == typeof(Dictionary<,>) || t == typeof(List<>))
                param.UsingGlobalTypes = false;
            // FEATURE : enable extensions when you can deserialize anon types
            if (param.EnableAnonymousTypes)
            {
                param.UseExtensions = false;
                param.UsingGlobalTypes = false;
            }

            using var serializer = new BjsonSerializer(param);
            return serializer.ConvertToBjson(obj);
        }

        /// <summary>
        /// Fills the members of an existing object from a payload instead of creating a new one.
        /// </summary>
        /// <param name="input">The object to fill.</param>
        /// <param name="json">The binary JSON bytes.</param>
        /// <returns>The filled object.</returns>
        /// <exception cref="ArgumentNullException">If <paramref name="json"/> or <paramref name="input"/> is null.</exception>
        /// <exception cref="BjsonException">If the payload names a type that cannot be created. Corrupt or truncated bytes can also throw other exception types.</exception>
        public static object? FillObject(object input, byte[] json)
        {
            Guard.NotNull(json, nameof(json));
            Guard.NotNull(input, nameof(input));
            return new Deserializer(Parameters.MakeCopy()).FillObject(input, json);
        }

        /// <summary>
        /// Deserializes a payload to <typeparamref name="T"/> using <see cref="Parameters"/>.
        /// </summary>
        /// <typeparam name="T">The type to create.</typeparam>
        /// <param name="json">The binary JSON bytes.</param>
        /// <returns>The restored value, or the default for a null payload.</returns>
        /// <exception cref="ArgumentNullException">If <paramref name="json"/> is null.</exception>
        /// <exception cref="BjsonException">If the payload names a type that cannot be created. Corrupt or truncated bytes can also throw other exception types.</exception>
        public static T? ToObject<T>(byte[] json)
        {
            Guard.NotNull(json, nameof(json));
            return new Deserializer(Parameters.MakeCopy()).ToObject<T>(json);
        }

        /// <summary>
        /// Deserializes a payload to <typeparamref name="T"/> using the given parameters for this call only.
        /// </summary>
        /// <typeparam name="T">The type to create.</typeparam>
        /// <param name="json">The binary JSON bytes.</param>
        /// <param name="param">The settings for this call.</param>
        /// <returns>The restored value, or the default for a null payload.</returns>
        /// <exception cref="ArgumentNullException">If <paramref name="json"/> or <paramref name="param"/> is null.</exception>
        /// <exception cref="BjsonException">If the payload names a type that cannot be created. Corrupt or truncated bytes can also throw other exception types.</exception>
        public static T? ToObject<T>(byte[] json, BjsonParameters param)
        {
            Guard.NotNull(json, nameof(json));
            Guard.NotNull(param, nameof(param));
            param = param.MakeCopy();
            param.FixValues();
            return new Deserializer(param).ToObject<T>(json);
        }

        /// <summary>
        /// Deserializes a payload to the type it was written as, using <see cref="Parameters"/>.
        /// </summary>
        /// <param name="json">The binary JSON bytes.</param>
        /// <returns>The restored value, or null for a null payload.</returns>
        /// <exception cref="ArgumentNullException">If <paramref name="json"/> is null.</exception>
        /// <exception cref="BjsonException">If the payload names a type that cannot be created. Corrupt or truncated bytes can also throw other exception types.</exception>
        public static object? ToObject(byte[] json)
        {
            Guard.NotNull(json, nameof(json));
            return new Deserializer(Parameters.MakeCopy()).ToObject(json, null);
        }

        /// <summary>
        /// Deserializes a payload to the type it was written as, using the given parameters for this call only.
        /// </summary>
        /// <param name="json">The binary JSON bytes.</param>
        /// <param name="param">The settings for this call.</param>
        /// <returns>The restored value, or null for a null payload.</returns>
        /// <exception cref="ArgumentNullException">If <paramref name="json"/> or <paramref name="param"/> is null.</exception>
        /// <exception cref="BjsonException">If the payload names a type that cannot be created. Corrupt or truncated bytes can also throw other exception types.</exception>
        public static object? ToObject(byte[] json, BjsonParameters param)
        {
            Guard.NotNull(json, nameof(json));
            Guard.NotNull(param, nameof(param));
            param = param.MakeCopy();
            param.FixValues();
            return new Deserializer(param).ToObject(json, null);
        }

        /// <summary>
        /// Deserializes a payload to the given type, using <see cref="Parameters"/>.
        /// </summary>
        /// <param name="json">The binary JSON bytes.</param>
        /// <param name="type">The type to create.</param>
        /// <returns>The restored value, or null for a null payload.</returns>
        /// <exception cref="ArgumentNullException">If <paramref name="json"/> or <paramref name="type"/> is null.</exception>
        /// <exception cref="BjsonException">If the payload names a type that cannot be created. Corrupt or truncated bytes can also throw other exception types.</exception>
        public static object? ToObject(byte[] json, Type type)
        {
            Guard.NotNull(json, nameof(json));
            Guard.NotNull(type, nameof(type));
            return new Deserializer(Parameters.MakeCopy()).ToObject(json, type);
        }

        /// <summary>
        /// Clears the internal reflection cache so every type is analysed again; the next calls are slower.
        /// </summary>
        public static void ClearReflectionCache()
        {
            TypeReflector.Instance.ClearReflectionCache();
        }

        /// <summary>
        /// Clones an object by serializing it and reading it back.
        /// </summary>
        /// <param name="obj">The object to copy; null is copied as null.</param>
        /// <returns>A new object with the same content.</returns>
        public static object? DeepCopy(object? obj)
        {
            return new Deserializer(Parameters.MakeCopy()).ToObject(ToBjson(obj));
        }
    }

    internal class Deserializer
    {
        /// <summary>
        /// Reads with <paramref name="param"/> as given: the caller passes a copy it owns, because
        /// resolving conflicting settings changes them.
        /// </summary>
        public Deserializer(BjsonParameters param)
        {
            _params = param;
        }

        private readonly BjsonParameters _params;

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
            object? read = ToObject(json, typeof(T));
            return read == null ? default : (T)read;
        }

        public object? ToObject(byte[] json)
        {
            return ToObject(json, null);
        }

        public object? ToObject(byte[] json, Type? type)
        {
            Type? t = null;
            if (type != null && type.IsGenericType)
                t = TypeReflector.Instance.GetGenericTypeDefinition(type);
            _globalTypes = _params.UsingGlobalTypes;
            if (t == typeof(Dictionary<,>) || t == typeof(List<>))
                _globalTypes = false;

            if (OneStep && type != null && TypedReader.TryRead(this, json, type, t, out object? read))
                return read;

            var o = new BjsonParser(json, _params.UseUtcDateTime, _params.UseV14TypedArray).Decode();
            if (type?.IsEnum == true)
                return CreateEnum(type, o!);
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

            if (o is List<object> list)
            {
                if (type != null && t == typeof(Dictionary<,>)) // kv format
                    return RootDictionary(o, type);

                if (type != null && t == typeof(List<>)) // deserialize to generic list
                    return RootList(o, type);

                if (type == typeof(Hashtable))
                    return RootHashTable(list);
                else if (type == null)
                {
                    List<object> l = list;
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
            else if (type != null && o != null && o.GetType() != type)
                return ChangeType(o, type);

            return o;
        }

        private static object? ChangeType(object? o, Type type)
        {
            if (TypeReflector.Instance.IsTypeRegistered(type))
                return TypeReflector.Instance.CreateCustom((string)o!, type);
            else
                return o;
        }

        public object? FillObject(object input, byte[] json)
        {
            _params.FixValues();
            Dictionary<string, object>? ht = new BjsonParser(json, _params.UseUtcDateTime, _params.UseV14TypedArray).Decode() as Dictionary<string, object>;
            if (ht == null)
                return null;
            return ParseDictionary(ht, null, input.GetType(), input);
        }

        private Hashtable RootHashTable(List<object> o)
        {
            Hashtable h = new Hashtable();

            foreach (Dictionary<string, object> values in o)
            {
                object? key = values["k"];
                object? val = values["v"];
                if (key is Dictionary<string, object> keyDictionary)
                    key = ParseDictionary(keyDictionary, null, typeof(object), null);

                if (val is Dictionary<string, object> valueDictionary)
                    val = ParseDictionary(valueDictionary, null, typeof(object), null);

                h.Add(key!, val);
            }

            return h;
        }

        private object RootList(object parse, Type? type)
        {
            Type[] gtypes = TypeReflector.Instance.GetGenericArguments(type!);
            IList o = (IList)TypeReflector.Instance.FastCreateList(type!, ((IList)parse).Count);
            Dictionary<string, object> globals = new Dictionary<string, object>();

            foreach (var k in (IList)parse)
            {
                _globalTypes = false;
                object? v;
                if (k is Dictionary<string, object> keyDictionary)
                    v = ParseDictionary(keyDictionary, globals, gtypes[0], null);
                else
                    v = k;

                o.Add(v);
            }
            return o;
        }

        private object? RootDictionary(object parse, Type type)
        {
            Type[] gtypes = TypeReflector.Instance.GetGenericArguments(type);
            Type t1 = gtypes[0];
            Type t2 = gtypes[1];
            var arraytype = t2.GetElementType();

            if (parse is Dictionary<string, object> parseDictionary)
            {
                IDictionary o = (IDictionary)TypeReflector.Instance.FastCreateInstance(type);

                foreach (var kv in parseDictionary)
                {
                    _globalTypes = false;
                    object? v;
                    object k = kv.Key;
                    if (t2.Name.StartsWith("Dictionary", StringComparison.Ordinal)) // deserialize a dictionary
                        v = RootDictionary(kv.Value, t2);
                    else if (kv.Value is Dictionary<string, object> valueDictionary)
                        v = ParseDictionary(valueDictionary, null, t2, null);
                    else if (t2 == typeof(byte[]))
                        v = kv.Value;
                    else if (gtypes != null && t2.IsArray)
                        v = CreateArray((List<object>)kv.Value, arraytype, null);
                    else if (kv.Value is IList)
                        v = CreateGenericList((List<object>)kv.Value, t2, t1, null);
                    else
                        v = kv.Value;

                    o.Add(k, v);
                }

                return o;
            }
            if (parse is List<object> parseList)
                return CreateDictionary(parseList, type, gtypes, null);

            return null;
        }

        private bool _globalTypes;

        #region Shared with TypedReader

        /*
         * Pieces of ParseDictionary that TypedReader has to perform identically - extracted rather
         * than duplicated, so the two paths cannot drift apart.
         */

        internal BjsonParameters Parameters => _params;

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

            return TypeReflector.Instance.FastCreateInstance(type);
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

            return TypeReflector.Instance.GetTypeFromCache((string)tn, true);
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

            Type? type = TypeReflector.Instance.GetTypeFromCache((string)entry, true);
            _lastTypeEntry = entry;
            _lastType = type;
            return type;
        }

        #endregion

        internal object? ParseDictionary(Dictionary<string, object>? d, Dictionary<string, object>? globaltypes, Type? type, object? input)
        {
            object? tn;
            if (type == typeof(NameValueCollection))
                return CreateNv(d!);
            if (type == typeof(StringDictionary))
                return CreateSd(d!);

            if (d!.TryGetValue("$i", out tn))
                return ResolveCircular(tn);

            if (d.TryGetValue("$types", out tn))
            {
                _globalTypes = true;
                if (globaltypes == null)
                    globaltypes = new Dictionary<string, object>();
                foreach (var kv in (Dictionary<string, object>)tn)
                {
                    globaltypes.Add(kv.Key, kv.Value);
                }
            }

            if (globaltypes != null)
                _globalTypes = true;

            // _globalTypes is always true here when globaltypes is non-null - set just above.
            // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract - a crafted payload can carry a null $type
            if (d.TryGetValue("$type", out tn) && tn != null)
                type = ResolveType(tn, _globalTypes ? globaltypes : null);
            else if (type == typeof(object))
                return d;

            if (type == null)
                throw new BjsonException("Cannot determine type");

            string typename = type.FullName!;
            object o = input ?? CreateInstance(type);
            int id = RegisterCircular(o);

            WireNameMap props = TypeReflector.Instance.GetWireNameMap(type, typename, _params.ShowReadOnlyProperties);
            foreach (var kv in d)
            {
                var v = kv.Value;
                PropertyMetadata? pi = props.Find(kv.Key);
                if (pi == null)
                    continue;
                if (pi.CanWrite && v != null)
                    o = pi.Setter!(o, ConvertValue(pi, v, globaltypes)!);
            }

            if (type.IsValueType)
                UpdateCircular(id, o);
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
        internal object? ConvertValue(PropertyMetadata pi, object v, Dictionary<string, object>? globaltypes)
        {
            if (v is TypedArray)
                return ParseTypedArray(globaltypes, v);

            switch (pi.Type)
            {
                case PropertyKind.DataSet:
                    return CreateDataset((Dictionary<string, object>)v, globaltypes);
                case PropertyKind.DataTable:
                    return CreateDataTable((Dictionary<string, object>)v, globaltypes);
                case PropertyKind.Custom:
                    return TypeReflector.Instance.CreateCustom((string)v, pi.Pt);
                case PropertyKind.Enum:
                    return CreateEnum(pi.Pt, v);
                case PropertyKind.SByte:
                    return CreateSByte(v);
                case PropertyKind.StringKeyDictionary:
                    return CreateStringKeyDictionary((Dictionary<string, object>)v, pi.Pt, pi.GenericTypes, globaltypes);
                case PropertyKind.Hashtable:
                case PropertyKind.Dictionary:
                    return CreateDictionary((List<object>)v, pi.Pt, pi.GenericTypes, globaltypes);
                case PropertyKind.NameValue:
                    return CreateNv((Dictionary<string, object>)v);
                case PropertyKind.StringDictionary:
                    return CreateSd((Dictionary<string, object>)v);
                case PropertyKind.Array:
                    return CreateArray((List<object>)v, pi.Bt, globaltypes);
            }

            if (pi.IsGenericType && !pi.IsValueType)
                return CreateGenericList((List<object>)v, pi.Pt, pi.Bt, globaltypes);

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
                return ParseDictionary(oo, globaltypes, pi.Pt, null);
            }

            if (v is List<object> valueList)
                return CreateArray(valueList, typeof(object), globaltypes);

            return v;
        }

        /// <summary>
        /// Restores an sbyte from either token, so streams written before Tokens.SByte still load.
        /// </summary>
        /// <remarks>
        /// A pre-fix writer emitted Tokens.Byte for an sbyte, so the parser hands back a byte and
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
            var t = TypeReflector.Instance.GetTypeFromCache(ta.TypeName, true);
            IList a = Array.CreateInstance(t!, ta.Count);
            int i = 0;
            foreach (var dd in ta.DataList)
            {
                object? oo;
                // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract - a typed array can hold null elements
                if (dd == null)
                    oo = null;
                else if (dd is TypedArray)
                    oo = ParseTypedArray(globaltypes, dd);
                else if (dd is Dictionary<string, object> ddDictionary)
                    oo = ParseDictionary(ddDictionary, globaltypes, t, null);
                else if (dd is List<object> ddList)
                    oo = CreateArray(ddList, t!.GetElementType(), globaltypes);
                else
                    oo = dd;
                a[i++] = oo;
            }
            oset = a;
            return oset;
        }

        private static StringDictionary CreateSd(Dictionary<string, object> d)
        {
            StringDictionary nv = new StringDictionary();

            foreach (var o in d)
                nv.Add(o.Key, (string)o.Value);

            return nv;
        }

        private static NameValueCollection CreateNv(Dictionary<string, object> d)
        {
            NameValueCollection nv = new NameValueCollection();

            foreach (var o in d)
                nv.Add(o.Key, (string)o.Value);

            return nv;
        }

        private static object CreateEnum(Type pt, object v)
        {
            // FEATURE : optimize create enum
            return Enum.Parse(pt, v.ToString()!);
        }

        /*
         * A schema in the payload is untrusted input: prohibiting the DTD and the resolver keeps a
         * crafted one from reading local files or reaching the network (XXE). The TextReader overload
         * of ReadXmlSchema applies neither.
         */
        private static XmlReader CreateSchemaReader(string schemaXml)
        {
            return XmlReader.Create(new StringReader(schemaXml), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        }

        private Array CreateArray(List<object> data, Type? bt, Dictionary<string, object>? globalTypes)
        {
            if (bt == null)
                bt = typeof(object);

            Array col = Array.CreateInstance(bt, data.Count);
            var arraytype = bt.GetElementType();
            // create an array of objects
            for (int i = 0; i < data.Count; i++) // each (object ob in data)
            {
                object ob = data[i];
                // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract - a list read from a payload can hold null elements
                if (ob == null)
                {
                    continue;
                }
                if (ob is IDictionary)
                    col.SetValue(ParseDictionary((Dictionary<string, object>)ob, globalTypes, bt, null), i);
                else if (ob is ICollection)
                    col.SetValue(CreateArray((List<object>)ob, arraytype, globalTypes), i);
                else
                    col.SetValue(ob, i);
            }

            return col;
        }

        private object CreateGenericList(List<object> data, Type pt, Type? bt, Dictionary<string, object>? globalTypes)
        {
            if (pt != typeof(object))
            {
                IList col = (IList)TypeReflector.Instance.FastCreateList(pt, data.Count);
                // create an array of objects
                foreach (object ob in data)
                {
                    if (ob is IDictionary)
                        col.Add(ParseDictionary((Dictionary<string, object>)ob, globalTypes, bt, null));
                    else if (ob is List<object> nestedList)
                    {
                        if (bt!.IsGenericType)
                            col.Add(nestedList);
                        else
                            col.Add(nestedList.ToArray());
                    }
                    else if (ob is TypedArray typedArray)
                        col.Add(typedArray.DataList.ToArray());
                    else
                        col.Add(ob);
                }
                return col;
            }
            return data;
        }

        private object CreateStringKeyDictionary(Dictionary<string, object> reader, Type pt, Type[]? types, Dictionary<string, object>? globalTypes)
        {
            var col = (IDictionary)TypeReflector.Instance.FastCreateInstance(pt);
            Type? arraytype;
            Type? t2 = null;
            if (types != null)
                t2 = types[1];

            Type? generictype = null;
            var ga = TypeReflector.Instance.GetGenericArguments(t2!);
            if (ga.Length > 0)
                generictype = ga[0];
            arraytype = t2!.GetElementType();

            foreach (KeyValuePair<string, object> values in reader)
            {
                var key = values.Key;
                object? val;

                if (values.Value is Dictionary<string, object> entryDictionary)
                    val = ParseDictionary(entryDictionary, globalTypes, t2, null);
                else if (types != null && t2.IsArray)
                {
                    if (values.Value is Array)
                        val = values.Value;
                    else
                        val = CreateArray((List<object>)values.Value, arraytype, globalTypes);
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
            IDictionary col = (IDictionary)TypeReflector.Instance.FastCreateInstance(pt);
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

                if (key is Dictionary<string, object> keyDictionary)
                    key = ParseDictionary(keyDictionary, globalTypes, t1, null);

                if (t2 != null && typeof(IDictionary).IsAssignableFrom(t2))
                    val = RootDictionary(val, t2);
                else if (val is Dictionary<string, object> valueDictionary)
                    val = ParseDictionary(valueDictionary, globalTypes, t2, null);

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

            if (schema is string schemaXml)
            {
                using XmlReader schemaReader = CreateSchemaReader(schemaXml);
                ds.ReadXmlSchema(schemaReader);
            }
            else
            {
                DatasetSchema ms = (DatasetSchema)ParseDictionary((Dictionary<string, object>)schema, globalTypes, typeof(DatasetSchema), null)!;
                ds.DataSetName = ms.Name!;
                for (int i = 0; i < ms.Info!.Count; i += 3)
                {
                    if (!ds.Tables.Contains(ms.Info[i]))
                        ds.Tables.Add(ms.Info[i]);
                    ds.Tables[ms.Info[i]]!.Columns.Add(ms.Info[i + 1], Type.GetType(ms.Info[i + 2])!);
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

        private static void ReadDataTable(List<object> rows, DataTable dt)
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

            if (schema is string schemaXml)
            {
                using XmlReader schemaReader = CreateSchemaReader(schemaXml);
                dt.ReadXmlSchema(schemaReader);
            }
            else
            {
                var ms = (DatasetSchema)this.ParseDictionary((Dictionary<string, object>)schema, globalTypes, typeof(DatasetSchema), null)!;
                dt.TableName = ms.Info![0];
                for (int i = 0; i < ms.Info.Count; i += 3)
                {
                    dt.Columns.Add(ms.Info[i + 1], Type.GetType(ms.Info[i + 2])!);
                }
            }

            foreach (var pair in reader)
            {
                if (pair.Key == "$type" || pair.Key == "$schema")
                    continue;

                var rows = (List<object>)pair.Value;
                if (rows == null)
                    continue;

                if (!dt.TableName.Equals(pair.Key, StringComparison.OrdinalIgnoreCase))
                    continue;

                ReadDataTable(rows, dt);
            }

            return dt;
        }
    }
}
