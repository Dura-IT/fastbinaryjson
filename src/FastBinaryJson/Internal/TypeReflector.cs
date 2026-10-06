using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Serialization;
using System.Text;
#if NET10_0_OR_GREATER
using System.Runtime.InteropServices;
#endif

namespace DuraIT.FastBinaryJson.Internal
{
    internal struct Getters
    {
        public string Name;
        public string? MemberName;
        public TypeReflector.GenericGetter Getter;
        public bool ReadOnly;

        /*
         * The member's key and colon as the writer writes them, per encoding, filled on first use.
         * Getters arrays are cached per type and shared between threads; a race only encodes the same
         * bytes twice. Internal because this struct is public and these are not part of it.
         */
        internal byte[]? KeyUtf16;
        internal byte[]? KeyUtf8;

        /*
         * The writer's boxing-free getter: a TypeReflector.TypedGetter<T> for a member whose type is
         * exactly the primitive T, and TypedToken, the token WriteValue writes T with. Null on every
         * other member, which keeps the boxing Getter.
         */
        internal Delegate? TypedGetter;
        internal byte TypedToken;
    }

    internal enum PropertyKind
    {
        Int,
        Long,
        String,
        Bool,
        DateTime,
        Enum,
        Guid,

        Array,
        ByteArray,
        Dictionary,
        StringKeyDictionary,
        NameValue,
        StringDictionary,
        Hashtable,
        DataSet,
        DataTable,
        Custom,
        Unknown,

        /*
         * Appended rather than grouped with the other primitives on purpose: this enum is public,
         * so inserting a member would renumber every one after it for anything already compiled
         * against the previous version.
         *
         * SByte is here because an sbyte property has to be told what to do with a byte. A stream
         * written before Tokens.SByte existed carries BYTE, and an unconverted byte cannot be
         * assigned to an sbyte property at all.
         */
        SByte,
    }

    internal sealed class PropertyMetadata
    {
        public Type Pt = null!;
        public Type? Bt;
        public Type? ChangeType;
        public TypeReflector.GenericSetter? Setter;
        public TypeReflector.GenericGetter? Getter;
        public Type[]? GenericTypes;
        public string Name = null!;
        public string? MemberName;
        public PropertyKind Type;
        public bool CanWrite;

        public bool IsClass;
        public bool IsValueType;
        public bool IsGenericType;
        public bool IsStruct;
        public bool IsInterface;

        /*
         * The one-step reader's boxing-free setter: a TypeReflector.TypedSetter<T> for the member's
         * primitive type T (or T?), and TypedToken, the token T is written with. Null on everything
         * else - non-primitive members, struct targets, static members - which only ever take the
         * boxing setter. Internal because this class is public and these are not part of it.
         */
        internal Delegate? TypedSetter;
        internal byte TypedToken;
    }

    internal sealed class TypeReflector
    {
        // Singleton pattern 4 from : http://csharpindepth.com/articles/general/singleton.aspx
        private static readonly TypeReflector SingleInstance = new TypeReflector();

        // Explicit static constructor to tell C# compiler
        // not to mark type as beforefieldinit
        static TypeReflector() { }

        private TypeReflector() { }

        public static TypeReflector Instance
        {
            get { return SingleInstance; }
        }

        public delegate object GenericSetter(object target, object value);
        internal delegate void TypedSetter<in T>(object target, T value);
        internal delegate T TypedGetter<out T>(object target);
        public delegate object? GenericGetter(object obj);
        private delegate object CreateObject();
        private delegate object CreateList(int capacity);

        private SafeDictionary<Type, string> _tyname = new SafeDictionary<Type, string>(10);
        private SafeDictionary<string, Type?> _typecache = new SafeDictionary<string, Type?>(10);
        private SafeDictionary<Type, CreateObject> _constrcache = new SafeDictionary<Type, CreateObject>(10);
        private readonly SafeDictionary<Type, CreateList?> _conlistcache = new SafeDictionary<Type, CreateList?>(10);
        private SafeDictionary<Type, Getters[]> _getterscache = new SafeDictionary<Type, Getters[]>(10);
        private SafeDictionary<string, Dictionary<string, PropertyMetadata>> _propertycache = new SafeDictionary<string, Dictionary<string, PropertyMetadata>>(
            10
        );

        // Companion of _propertycache, same key, reset wherever it is.
        private SafeDictionary<string, WireNameMap> _wirenamecache = new SafeDictionary<string, WireNameMap>(10);
        private SafeDictionary<Type, Type[]> _genericTypes = new SafeDictionary<Type, Type[]>(10);
        private SafeDictionary<Type, Type> _genericTypeDef = new SafeDictionary<Type, Type>(10);
        private static SafeDictionary<short, OpCode>? _opCodes;
        private static List<string> _blacklistTypes = new List<string>()
        {
            "system.configuration.install.assemblyinstaller",
            "system.activities.presentation.workflowdesigner",
            "system.windows.resourcedictionary",
            "system.windows.data.objectdataprovider",
            "system.windows.forms.bindingsource",
            "microsoft.exchange.management.systemmanager.winforms.exchangesettingsprovider",
        };

        private static bool TryGetOpCode(short code, out OpCode opCode)
        {
            if (_opCodes != null)
                return _opCodes.TryGetValue(code, out opCode);
            var dict = new SafeDictionary<short, OpCode>();
            foreach (var fi in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (!typeof(OpCode).IsAssignableFrom(fi.FieldType))
                    continue;
                var innerOpCode = (OpCode)fi.GetValue(null)!;
                if (innerOpCode.OpCodeType != OpCodeType.Nternal)
                    dict.Add(innerOpCode.Value, innerOpCode);
            }
            _opCodes = dict;
            return _opCodes.TryGetValue(code, out opCode);
        }

        #region bjson custom types
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding();

        public static byte[] Utf8GetBytes(string str)
        {
            return Utf8.GetBytes(str);
        }

#if NET10_0_OR_GREATER
        /*
         * Span overloads over the SAME encoder instance as Utf8GetBytes, so invalid surrogates get
         * the same replacement fallback and the bytes cannot drift between the two paths.
         */
        internal static int Utf8GetByteCount(string str) => Utf8.GetByteCount(str);

        internal static int Utf8GetBytes(string str, Span<byte> destination) => Utf8.GetBytes(str, destination);

        internal static int Utf8GetChars(ReadOnlySpan<byte> bytes, Span<char> destination) => Utf8.GetChars(bytes, destination);
#endif

        public static string Utf8GetString(byte[] bytes, int offset, int len)
        {
            return Utf8.GetString(bytes, offset, len);
        }

        public static byte[] UnicodeGetBytes(string str)
        {
#if NET10_0_OR_GREATER
            return MemoryMarshal.AsBytes(str.AsSpan()).ToArray();
#else
            // netstandard2.0 has no Span, and unsafe is off: copy through a per-thread scratch array so the
            // only allocation is the result, as the pointer copy this replaced had.
            char[] scratch = RentScratch(str.Length);
            str.CopyTo(0, scratch, 0, str.Length);
            byte[] b = new byte[str.Length * 2];
            Buffer.BlockCopy(scratch, 0, b, 0, b.Length);
            return b;
#endif
        }

        public static string UnicodeGetString(byte[] b)
        {
            return UnicodeGetString(b, 0, b.Length);
        }

        public static string UnicodeGetString(byte[] bytes, int offset, int buflen)
        {
            // Bounds are checked here so a length running past the payload cannot be read as text.
            if (offset < 0 || buflen < 0 || offset > bytes.Length - buflen)
                throw new ArgumentOutOfRangeException(nameof(buflen));

#if NET10_0_OR_GREATER
            // One allocation: the string is built straight from the bytes, and an odd trailing byte is dropped.
            return new string(MemoryMarshal.Cast<byte, char>(new ReadOnlySpan<byte>(bytes, offset, buflen & ~1)));
#else
            int count = buflen / 2;
            char[] scratch = RentScratch(count);
            Buffer.BlockCopy(bytes, offset, scratch, 0, count * 2);
            return new string(scratch, 0, count);
#endif
        }

#if !NET10_0_OR_GREATER
        private const int MaxScratchChars = 32 * 1024;

        [ThreadStatic]
        private static char[]? _scratch;

        /// <summary>
        /// A per-thread char array of at least <paramref name="length"/>; contents are not cleared. Not kept
        /// past <see cref="MaxScratchChars"/>, so one huge string does not pin its memory to the thread.
        /// </summary>
        private static char[] RentScratch(int length)
        {
            if (length > MaxScratchChars)
                return new char[length];
            char[]? scratch = _scratch;
            if (scratch == null || scratch.Length < length)
            {
                scratch = new char[Math.Max(length, 256)];
                _scratch = scratch;
            }
            return scratch;
        }
#endif
        #endregion

        #region json custom types
        // JSON custom
        internal SafeDictionary<Type, CustomTypeSerializer> CustomSerializer = new SafeDictionary<Type, CustomTypeSerializer>();
        internal SafeDictionary<Type, CustomTypeDeserializer> CustomDeserializer = new SafeDictionary<Type, CustomTypeDeserializer>();

        /*
         * Resolution results per exact type, misses included, so the base-chain walk below is paid
         * once per type instead of on every value written.
         *
         * This matters on the hot path rather than in theory: IsTypeRegistered is consulted for
         * every object that reaches the fallback in WriteValue, so without a cache a deep hierarchy
         * with any registration present would walk its whole chain per object, every time.
         */
        private SafeDictionary<Type, CustomTypeSerializer?> _serializerForType = new SafeDictionary<Type, CustomTypeSerializer?>();
        private SafeDictionary<Type, CustomTypeDeserializer?> _deserializerForType = new SafeDictionary<Type, CustomTypeDeserializer?>();

        internal object CreateCustom(string v, Type type)
        {
            TryGetCustomDeserializer(type, out CustomTypeDeserializer? d);
            return d!(v);
        }

        internal void RegisterCustomType(Type type, CustomTypeSerializer serializer, CustomTypeDeserializer deserializer)
        {
            CustomSerializer.Add(type, serializer);
            CustomDeserializer.Add(type, deserializer);
            // A new registration can shadow a base one and can turn an earlier miss into a hit,
            // so the resolved results are no longer trustworthy.
            _serializerForType = new SafeDictionary<Type, CustomTypeSerializer?>();
            _deserializerForType = new SafeDictionary<Type, CustomTypeDeserializer?>();
            _plainObject = new SafeDictionary<Type, bool>();
            // reset property cache
            Instance.ResetPropertyCache();
        }

        internal bool TryGetCustomSerializer(Type t, out CustomTypeSerializer? serializer)
        {
            serializer = null;
            if (CustomSerializer.Count() == 0)
                return false;

            if (!_serializerForType.TryGetValue(t, out serializer))
            {
                serializer = Resolve(t, CustomSerializer);
                _serializerForType.Add(t, serializer);
            }

            return serializer != null;
        }

        internal bool TryGetCustomDeserializer(Type t, out CustomTypeDeserializer? deserializer)
        {
            deserializer = null;
            if (CustomDeserializer.Count() == 0)
                return false;

            if (!_deserializerForType.TryGetValue(t, out deserializer))
            {
                deserializer = Resolve(t, CustomDeserializer);
                _deserializerForType.Add(t, deserializer);
            }

            return deserializer != null;
        }

        /// <summary>
        /// Finds the registration for <paramref name="t"/> or, failing that, for its nearest
        /// registered base type.
        /// </summary>
        /// <remarks>
        /// Upstream matched the exact runtime type only, which silently skipped every derived
        /// instance - including the framework's own, since IPAddress.Loopback returns a private
        /// ReadOnlyIPAddress subclass on .NET.
        ///
        /// Base classes only. Interfaces are not walked: a type can implement several registered
        /// interfaces with no defensible way to choose between them, and the exact match never
        /// covered them either. Walking stops naturally at object, which nothing sane registers.
        /// </remarks>
        private static TRegistration? Resolve<TRegistration>(Type t, SafeDictionary<Type, TRegistration> registrations)
            where TRegistration : class
        {
            Type? candidate = t;
            while (candidate != null)
            {
                if (registrations.TryGetValue(candidate, out TRegistration? found))
                    return found;

                candidate = candidate.BaseType;
            }

            return null;
        }

        /// <summary>
        /// Forgets every registered custom type.
        /// </summary>
        /// <remarks>
        /// Deliberately not folded into ClearReflectionCache, which consumers call to drop cached
        /// reflection: dropping their registrations as a side effect of that would be a behaviour
        /// change. This exists so a test that registers a type can undo it - registration is
        /// process-wide and there was previously no way back, which made every custom-type test
        /// dependent on the order NUnit happened to run the fixtures in.
        /// </remarks>
        internal void ClearCustomTypes()
        {
            CustomSerializer = new SafeDictionary<Type, CustomTypeSerializer>();
            CustomDeserializer = new SafeDictionary<Type, CustomTypeDeserializer>();
            _serializerForType = new SafeDictionary<Type, CustomTypeSerializer?>();
            _deserializerForType = new SafeDictionary<Type, CustomTypeDeserializer?>();
            _plainObject = new SafeDictionary<Type, bool>();
            ResetPropertyCache();
        }

        private static bool NameContainsDictionary(Type t)
        {
#if NET10_0_OR_GREATER
            return t.Name.Contains("Dictionary", StringComparison.Ordinal);
#else
            return t.Name.IndexOf("Dictionary", StringComparison.Ordinal) >= 0;
#endif
        }

        internal bool IsTypeRegistered(Type t)
        {
            return TryGetCustomSerializer(t, out _);
        }

        /*
         * Whether WriteValue would send a value of this exact type through every special case and land
         * on WriteObject: not a dictionary, collection, DataSet, DataTable, enum or DateTimeOffset, and
         * not registered as a custom type. Decided once per type from the same questions the `is` chain
         * asks, so an ordinary object costs one lookup instead of that whole chain - the interface
         * casts in it are the slow part. Reset whenever a registration changes the answer.
         */
        private SafeDictionary<Type, bool> _plainObject = new SafeDictionary<Type, bool>();

        internal bool IsPlainObject(Type t)
        {
            if (_plainObject.TryGetValue(t, out bool plain))
                return plain;

            plain =
                !typeof(IEnumerable).IsAssignableFrom(t)
                && !typeof(DataSet).IsAssignableFrom(t)
                && !typeof(DataTable).IsAssignableFrom(t)
                && !typeof(Enum).IsAssignableFrom(t)
                && !typeof(StringDictionary).IsAssignableFrom(t)
                && !typeof(NameValueCollection).IsAssignableFrom(t)
                && t != typeof(DateTimeOffset)
                && !IsTypeRegistered(t);
            _plainObject.Add(t, plain);
            return plain;
        }
        #endregion

        public Type GetGenericTypeDefinition(Type t)
        {
            Type? tt;
            if (_genericTypeDef.TryGetValue(t, out tt))
                return tt!;
            else
            {
                tt = t.GetGenericTypeDefinition();
                _genericTypeDef.Add(t, tt);
                return tt;
            }
        }

        public Type[] GetGenericArguments(Type t)
        {
            Type[]? tt;
            if (_genericTypes.TryGetValue(t, out tt))
                return tt!;
            else
            {
                tt = t.GetGenericArguments();
                _genericTypes.Add(t, tt);
                return tt;
            }
        }

        public Dictionary<string, PropertyMetadata> Getproperties(Type type, string typename, bool showReadOnlyProperties)
        {
            Dictionary<string, PropertyMetadata>? sd;
            if (_propertycache.TryGetValue(typename, out sd))
            {
                return sd!;
            }
            else
            {
                sd = new Dictionary<string, PropertyMetadata>(10, StringComparer.OrdinalIgnoreCase);
                /*
                 * Lookups ignore case because a wire key is matched whatever case it was written in. A [DataMember(Name = ...)] member is keyed under that name, and also under
                 * its C# name as an alias: the writer only started writing DataMember names in this
                 * fork, so everything stored before that carries the C# name. Aliases go in after
                 * every primary key and only where the key is still free, so an alias can never take
                 * a name that belongs to another member.
                 */
                List<KeyValuePair<string, PropertyMetadata>> aliases = new List<KeyValuePair<string, PropertyMetadata>>();
                var bf = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static;
                PropertyInfo[] pr = type.GetProperties(bf);
                foreach (PropertyInfo p in pr)
                {
                    if (p.GetIndexParameters().Length > 0) // Property is an indexer
                        continue;

                    PropertyMetadata d = CreateMyProp(p.PropertyType, p.Name);
                    d.Setter = TypeReflector.CreateSetMethod(type, p, showReadOnlyProperties);
                    if (d.Setter != null)
                    {
                        d.CanWrite = true;
                        AddTypedSetter(d, type, p, showReadOnlyProperties);
                    }
                    d.Getter = TypeReflector.CreateGetMethod(type, p);
                    var att = p.GetCustomAttributes(true);
                    foreach (var at in att)
                    {
                        if (at is DataMemberAttribute dm && !string.IsNullOrEmpty(dm.Name))
                        {
                            d.MemberName = dm.Name;
                        }
                    }
                    AddMemberKeys(sd, aliases, d, p.Name);
                }
                FieldInfo[] fi = type.GetFields(bf);
                foreach (FieldInfo f in fi)
                {
                    PropertyMetadata d = CreateMyProp(f.FieldType, f.Name);
                    if (!f.IsLiteral)
                    {
                        if (!f.IsInitOnly)
                            d.Setter = TypeReflector.CreateSetField(type, f);
                        if (d.Setter != null)
                        {
                            d.CanWrite = true;
                            AddTypedSetter(d, type, f);
                        }
                        d.Getter = TypeReflector.CreateGetField(type, f);
                        var att = f.GetCustomAttributes(true);
                        foreach (var at in att)
                        {
                            if (at is DataMemberAttribute dm && !string.IsNullOrEmpty(dm.Name))
                            {
                                d.MemberName = dm.Name;
                            }
                        }
                        AddMemberKeys(sd, aliases, d, f.Name);
                    }
                }

                foreach (KeyValuePair<string, PropertyMetadata> alias in aliases.Where(alias => !sd.ContainsKey(alias.Key)))
                {
                    sd.Add(alias.Key, alias.Value);
                }

                _propertycache.Add(typename, sd);
                return sd;
            }
        }

        private static void AddMemberKeys(
            Dictionary<string, PropertyMetadata> sd,
            List<KeyValuePair<string, PropertyMetadata>> aliases,
            PropertyMetadata d,
            string name
        )
        {
            if (d.MemberName == null)
            {
                sd.Add(name, d);
                return;
            }

            sd.Add(d.MemberName, d);
            aliases.Add(new KeyValuePair<string, PropertyMetadata>(name, d));
        }

        internal WireNameMap GetWireNameMap(Type type, string typename, bool showReadOnlyProperties)
        {
            if (_wirenamecache.TryGetValue(typename, out WireNameMap? map))
                return map!;

            map = new WireNameMap(Getproperties(type, typename, showReadOnlyProperties));
            _wirenamecache.Add(typename, map);
            return map;
        }

        private PropertyMetadata CreateMyProp(Type t, string name)
        {
            PropertyMetadata d = new PropertyMetadata();
            PropertyKind dType = PropertyKind.Unknown;

            if (t == typeof(int) || t == typeof(int?))
                dType = PropertyKind.Int;
            else if (t == typeof(long) || t == typeof(long?))
                dType = PropertyKind.Long;
            else if (t == typeof(string))
                dType = PropertyKind.String;
            else if (t == typeof(bool) || t == typeof(bool?))
                dType = PropertyKind.Bool;
            else if (t == typeof(DateTime) || t == typeof(DateTime?))
                dType = PropertyKind.DateTime;
            else if (t.IsEnum)
                dType = PropertyKind.Enum;
            else if (t == typeof(Guid) || t == typeof(Guid?))
                dType = PropertyKind.Guid;
            else if (t == typeof(sbyte) || t == typeof(sbyte?))
                dType = PropertyKind.SByte;
            else if (t == typeof(StringDictionary))
                dType = PropertyKind.StringDictionary;
            else if (t == typeof(NameValueCollection))
                dType = PropertyKind.NameValue;
            else if (t.IsArray)
            {
                d.Bt = t.GetElementType();
                if (t == typeof(byte[]))
                    dType = PropertyKind.ByteArray;
                else
                    dType = PropertyKind.Array;
            }
            else if (NameContainsDictionary(t))
            {
                d.GenericTypes = TypeReflector.Instance.GetGenericArguments(t);
                if (d.GenericTypes.Length > 0 && d.GenericTypes[0] == typeof(string))
                    dType = PropertyKind.StringKeyDictionary;
                else
                    dType = PropertyKind.Dictionary;
            }
            else if (t == typeof(Hashtable))
                dType = PropertyKind.Hashtable;
            else if (t == typeof(DataSet))
                dType = PropertyKind.DataSet;
            else if (t == typeof(DataTable))
                dType = PropertyKind.DataTable;
            else if (IsTypeRegistered(t))
                dType = PropertyKind.Custom;

            if (t.IsValueType && !t.IsPrimitive && !t.IsEnum && t != typeof(decimal))
                d.IsStruct = true;

            d.IsInterface = t.IsInterface;
            d.IsClass = t.IsClass;
            d.IsValueType = t.IsValueType;
            if (t.IsGenericType)
            {
                d.IsGenericType = true;
                d.Bt = TypeReflector.Instance.GetGenericArguments(t)[0];
            }

            d.Pt = t;
            d.Name = name;
            d.ChangeType = GetChangeType(t);
            d.Type = dType;

            return d;
        }

        private static Type GetChangeType(Type conversionType)
        {
            if (conversionType.IsGenericType && conversionType.GetGenericTypeDefinition().Equals(typeof(Nullable<>)))
                return TypeReflector.Instance.GetGenericArguments(conversionType)[0];

            return conversionType;
        }

        #region [   PROPERTY GET SET   ]

        public string GetTypeAssemblyName(Type t)
        {
            string? val;
            if (_tyname.TryGetValue(t, out val))
                return val!;
            else
            {
                string s = t == typeof(DatasetSchema) ? UpstreamDatasetSchemaName : t.AssemblyQualifiedName!;
                _tyname.Add(t, s);
                return s;
            }
        }

        /*
         * The embedded DataSet/DataTable schema is a DatasetSchema object, so its $type is a library type name.
         * Upstream wrote its own; this package has another assembly name. Writing upstream's name and
         * resolving it back to this type keeps DataSet and DataTable payloads readable in both directions.
         */
        private const string UpstreamDatasetSchemaPrefix = "fastBinaryJSON.DatasetSchema,";

        private const string UpstreamDatasetSchemaName =
            "fastBinaryJSON.DatasetSchema, fastBinaryJSON, Version=1.5.0.0, Culture=neutral, PublicKeyToken=6b75a806b86095cd";

        internal Type? GetTypeFromCache(string typename, bool blacklistChecking)
        {
            Type? val;
            if (_typecache.TryGetValue(typename, out val))
                return val;
            else
            {
                // check for BLACK LIST types -> more secure when using $type
                if (blacklistChecking)
                {
                    var tn = typename.Trim();
                    if (_blacklistTypes.Any(s => tn.StartsWith(s, StringComparison.OrdinalIgnoreCase)))
                        throw new BjsonException("Black list type encountered, possible attack vector when using $type : " + typename);
                }

                Type? t = typename.StartsWith(UpstreamDatasetSchemaPrefix, StringComparison.Ordinal) ? typeof(DatasetSchema) : Type.GetType(typename);
                _typecache.Add(typename, t);
                return t;
            }
        }

        internal object FastCreateList(Type objtype, int capacity)
        {
            try
            {
                int count = 10;
                if (capacity > 10)
                    count = capacity;
                CreateList? c;
                if (_conlistcache.TryGetValue(objtype, out c))
                {
                    if (c != null) // kludge : non capacity lists
                        return c(count);
                    else
                        return FastCreateInstance(objtype);
                }
                else
                {
                    var cinfo = objtype.GetConstructor(new[] { typeof(int) });
                    if (cinfo != null)
                    {
                        DynamicMethod dynMethod = new DynamicMethod("_fcil", objtype, new[] { typeof(int) }, true);
                        ILGenerator ilGen = dynMethod.GetILGenerator();
                        ilGen.Emit(OpCodes.Ldarg_0);
                        ilGen.Emit(OpCodes.Newobj, objtype.GetConstructor(new[] { typeof(int) })!);
                        ilGen.Emit(OpCodes.Ret);
                        c = (CreateList)dynMethod.CreateDelegate(typeof(CreateList));
                        _conlistcache.Add(objtype, c);
                        return c(count);
                    }
                    else
                    {
                        _conlistcache.Add(objtype, null); // kludge : non capacity lists
                        return FastCreateInstance(objtype);
                    }
                }
            }
            catch (Exception exc)
            {
                throw new BjsonException(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "Failed to fast create instance for type '{0}' from assembly '{1}'",
                        objtype.FullName,
                        objtype.AssemblyQualifiedName
                    ),
                    exc
                );
            }
        }

        internal object FastCreateInstance(Type objtype)
        {
            try
            {
                CreateObject? c;
                if (_constrcache.TryGetValue(objtype, out c))
                {
                    return c!();
                }
                else
                {
                    if (objtype.IsClass)
                    {
                        DynamicMethod dynMethod = new DynamicMethod("_fcic", objtype, null, true);
                        ILGenerator ilGen = dynMethod.GetILGenerator();
                        ilGen.Emit(OpCodes.Newobj, objtype.GetConstructor(Type.EmptyTypes)!);
                        ilGen.Emit(OpCodes.Ret);
                        c = (CreateObject)dynMethod.CreateDelegate(typeof(CreateObject));
                        _constrcache.Add(objtype, c);
                    }
                    else // structs
                    {
                        DynamicMethod dynMethod = new DynamicMethod("_fcis", typeof(object), null, true);
                        ILGenerator ilGen = dynMethod.GetILGenerator();
                        var lv = ilGen.DeclareLocal(objtype);
                        ilGen.Emit(OpCodes.Ldloca_S, lv);
                        ilGen.Emit(OpCodes.Initobj, objtype);
                        ilGen.Emit(OpCodes.Ldloc_0);
                        ilGen.Emit(OpCodes.Box, objtype);
                        ilGen.Emit(OpCodes.Ret);
                        c = (CreateObject)dynMethod.CreateDelegate(typeof(CreateObject));
                        _constrcache.Add(objtype, c);
                    }
                    return c();
                }
            }
            catch (Exception exc)
            {
                throw new BjsonException(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "Failed to fast create instance for type '{0}' from assembly '{1}'",
                        objtype.FullName,
                        objtype.AssemblyQualifiedName
                    ),
                    exc
                );
            }
        }

        // Writes through what CreateSetMethod writes through: the set method, or the backing field it falls back to.
        private static void AddTypedSetter(PropertyMetadata d, Type type, PropertyInfo property, bool showReadOnlyProperties)
        {
            MethodInfo? setMethod = property.GetSetMethod(showReadOnlyProperties);
            if (setMethod == null)
            {
                FieldInfo? backingField = showReadOnlyProperties ? GetGetterBackingField(property) : null;
                if (backingField != null)
                    AddTypedSetter(d, type, backingField);
                return;
            }

            if (!setMethod.IsStatic)
                BuildTypedSetter(d, type, property.PropertyType, property.DeclaringType!, setMethod, null);
        }

        private static void AddTypedSetter(PropertyMetadata d, Type type, FieldInfo field)
        {
            if (!field.IsStatic)
                BuildTypedSetter(d, type, field.FieldType, field.DeclaringType!, null, field);
        }

        /// <summary>
        /// Builds the boxing-free setter the one-step reader uses for a primitive member of a class.
        /// </summary>
        /// <remarks>
        /// The IL is the class branch of CreateSetMethod / CreateSetField with the argument typed
        /// instead of unboxed, plus - for a T? member - the Nullable constructor that unbox.any to T?
        /// runs implicitly. Struct targets are left out: their boxing setter copies the struct out of
        /// its box and boxes it again per member, so an unboxed value would save nothing there.
        /// </remarks>
        private static void BuildTypedSetter(PropertyMetadata d, Type type, Type memberType, Type declaringType, MethodInfo? setMethod, FieldInfo? field)
        {
            if (!type.IsClass)
                return;

            Type? underlying = Nullable.GetUnderlyingType(memberType);
            Type valueType = underlying ?? memberType;
            byte token = TypedToken(valueType);
            if (token == 0)
                return;

            DynamicMethod method = new DynamicMethod("_cts", typeof(void), new[] { typeof(object), valueType }, type, true);
            ILGenerator il = method.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Castclass, declaringType);
            il.Emit(OpCodes.Ldarg_1);
            if (underlying != null)
                il.Emit(OpCodes.Newobj, memberType.GetConstructor(new[] { valueType })!);
            if (setMethod != null)
                il.EmitCall(OpCodes.Callvirt, setMethod, null);
            else
                il.Emit(OpCodes.Stfld, field!);
            il.Emit(OpCodes.Ret);

            d.TypedSetter = method.CreateDelegate(typeof(TypedSetter<>).MakeGenericType(valueType));
            d.TypedToken = token;
        }

        /*
         * The token each primitive is written with, which is the one token the parser turns into
         * exactly that type. 0 for every type the one-step reader leaves to the boxing setter: sbyte
         * and enums, because ConvertValue converts them; strings and everything else, because they
         * are references and were never boxed. bool is keyed on TRUE and stands for FALSE too.
         */
        private static byte TypedToken(Type t)
        {
            if (t == typeof(int))
                return Tokens.Int32;
            if (t == typeof(long))
                return Tokens.Int64;
            if (t == typeof(bool))
                return Tokens.True;
            if (t == typeof(DateTime))
                return Tokens.DateTime;
            if (t == typeof(Guid))
                return Tokens.Guid;
            if (t == typeof(double))
                return Tokens.Double;
            if (t == typeof(float))
                return Tokens.Single;
            if (t == typeof(decimal))
                return Tokens.Decimal;
            if (t == typeof(short))
                return Tokens.Int16;
            if (t == typeof(ushort))
                return Tokens.UInt16;
            if (t == typeof(uint))
                return Tokens.UInt32;
            if (t == typeof(ulong))
                return Tokens.UInt64;
            if (t == typeof(byte))
                return Tokens.Byte;
            if (t == typeof(char))
                return Tokens.Char;
            if (t == typeof(TimeSpan))
                return Tokens.TimeSpan;
            if (t == typeof(DateTimeOffset))
                return Tokens.DateTimeOffset;
            return 0;
        }

        /// <summary>
        /// Builds the boxing-free getter the writer uses for a primitive member of a class.
        /// </summary>
        /// <remarks>
        /// The class branch of CreateGetMethod / CreateGetField without the box. Struct targets and
        /// static members keep the boxing getter only, as they do for the typed setters.
        /// </remarks>
        private static void BuildTypedGetter(ref Getters getter, Type type, Type memberType, Type declaringType, MethodInfo? getMethod, FieldInfo? field)
        {
            if (!type.IsClass || getMethod?.IsStatic == true || field?.IsStatic == true)
                return;

            byte token = WrittenToken(memberType);
            if (token == 0)
                return;

            DynamicMethod method = new DynamicMethod("_ctg", memberType, new[] { typeof(object) }, type, true);
            ILGenerator il = method.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Castclass, declaringType);
            if (getMethod != null)
                il.EmitCall(OpCodes.Callvirt, getMethod, null);
            else
                il.Emit(OpCodes.Ldfld, field!);
            il.Emit(OpCodes.Ret);

            getter.TypedGetter = method.CreateDelegate(typeof(TypedGetter<>).MakeGenericType(memberType));
            getter.TypedToken = token;
        }

        /*
         * The token WriteValue writes a value of exactly this type with. Every type here is matched
         * by its own branch of WriteValue's chain, ahead of the custom-type check, so a boxed value of
         * it can only take that branch. 0 for everything else: nullables (they can be null), enums,
         * strings, and DateTimeOffset, which WriteValue checks after custom types.
         */
        private static byte WrittenToken(Type t)
        {
            if (t == typeof(sbyte))
                return Tokens.SByte;
            if (t == typeof(DateTimeOffset))
                return 0;

            return TypedToken(t);
        }

        internal static GenericSetter CreateSetField(Type type, FieldInfo fieldInfo)
        {
            Type[] arguments = new Type[2];
            arguments[0] = arguments[1] = typeof(object);

            DynamicMethod dynamicSet = new DynamicMethod("_csf", typeof(object), arguments, type, true);

            ILGenerator il = dynamicSet.GetILGenerator();

            if (!type.IsClass) // structs
            {
                var lv = il.DeclareLocal(type);
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Unbox_Any, type);
                il.Emit(OpCodes.Stloc_0);
                il.Emit(OpCodes.Ldloca_S, lv);
                il.Emit(OpCodes.Ldarg_1);
                if (fieldInfo.FieldType.IsClass)
                    il.Emit(OpCodes.Castclass, fieldInfo.FieldType);
                else
                    il.Emit(OpCodes.Unbox_Any, fieldInfo.FieldType);
                il.Emit(OpCodes.Stfld, fieldInfo);
                il.Emit(OpCodes.Ldloc_0);
                il.Emit(OpCodes.Box, type);
                il.Emit(OpCodes.Ret);
            }
            else
            {
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldarg_1);
                if (fieldInfo.FieldType.IsValueType)
                    il.Emit(OpCodes.Unbox_Any, fieldInfo.FieldType);
                il.Emit(OpCodes.Stfld, fieldInfo);
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ret);
            }
            return (GenericSetter)dynamicSet.CreateDelegate(typeof(GenericSetter));
        }

        internal static FieldInfo? GetGetterBackingField(PropertyInfo autoProperty)
        {
            var getMethod = autoProperty.GetGetMethod();
            // Restrict operation to auto properties to avoid risking errors if a getter does not contain exactly one field read instruction (such as with calculated properties).
            if (!getMethod!.IsDefined(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute), false))
                return null;

            var byteCode = getMethod.GetMethodBody()?.GetILAsByteArray() ?? Array.Empty<byte>();
            int pos = 0;
            // Find the first LdFld instruction and parse its operand to a FieldInfo object.
            while (pos < byteCode.Length)
            {
                // Read and parse the OpCode (it can be 1 or 2 bytes in size).
                byte code = byteCode[pos++];
                if (!(TryGetOpCode(code, out var opCode) || pos < byteCode.Length && TryGetOpCode((short)(code * 0x100 + byteCode[pos++]), out opCode)))
                    throw new NotSupportedException("Unknown IL code detected.");
                // If it is a LdFld, read its operand, parse it to a FieldInfo and return it.
                if (opCode == OpCodes.Ldfld && opCode.OperandType == OperandType.InlineField && pos + sizeof(int) <= byteCode.Length)
                {
                    return getMethod.Module.ResolveMember(BitConverter.ToInt32(byteCode, pos), getMethod.DeclaringType?.GetGenericArguments(), null)
                        as FieldInfo;
                }
                // Otherwise, set the current position to the start of the next instruction, if any (we need to know how much bytes are used by operands).
                pos += opCode.OperandType switch
                {
                    OperandType.InlineNone => 0,
                    OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                    OperandType.InlineVar => 2,
                    OperandType.InlineI8 or OperandType.InlineR => 8,
                    OperandType.InlineSwitch => 4 * (BitConverter.ToInt32(byteCode, pos) + 1),
                    _ => 4,
                };
            }
            return null;
        }

        internal static GenericSetter? CreateSetMethod(Type type, PropertyInfo propertyInfo, bool showReadOnlyProperties)
        {
            MethodInfo? setMethod = propertyInfo.GetSetMethod(showReadOnlyProperties);
            if (setMethod == null)
            {
                if (!showReadOnlyProperties)
                    return null;
                // If the property has no setter and it is an auto property, try and create a setter for its backing field instead
                var fld = GetGetterBackingField(propertyInfo);
                return fld != null ? CreateSetField(type, fld) : null;
            }

            Type[] arguments = new Type[2];
            arguments[0] = arguments[1] = typeof(object);

            DynamicMethod setter = new DynamicMethod("_csm", typeof(object), arguments, true); // !setMethod.IsPublic); // fix: skipverify
            ILGenerator il = setter.GetILGenerator();

            if (!type.IsClass) // structs
            {
                var lv = il.DeclareLocal(type);
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Unbox_Any, type);
                il.Emit(OpCodes.Stloc_0);
                il.Emit(OpCodes.Ldloca_S, lv);
                il.Emit(OpCodes.Ldarg_1);
                if (propertyInfo.PropertyType.IsClass)
                    il.Emit(OpCodes.Castclass, propertyInfo.PropertyType);
                else
                    il.Emit(OpCodes.Unbox_Any, propertyInfo.PropertyType);
                il.EmitCall(OpCodes.Call, setMethod, null);
                il.Emit(OpCodes.Ldloc_0);
                il.Emit(OpCodes.Box, type);
            }
            else
            {
                if (!setMethod.IsStatic)
                {
                    il.Emit(OpCodes.Ldarg_0);
                    il.Emit(OpCodes.Castclass, propertyInfo.DeclaringType!);
                    il.Emit(OpCodes.Ldarg_1);
                    if (propertyInfo.PropertyType.IsClass)
                        il.Emit(OpCodes.Castclass, propertyInfo.PropertyType);
                    else
                        il.Emit(OpCodes.Unbox_Any, propertyInfo.PropertyType);
                    il.EmitCall(OpCodes.Callvirt, setMethod, null);
                    il.Emit(OpCodes.Ldarg_0);
                }
                else
                {
                    il.Emit(OpCodes.Ldarg_0);
                    il.Emit(OpCodes.Ldarg_1);
                    if (propertyInfo.PropertyType.IsClass)
                        il.Emit(OpCodes.Castclass, propertyInfo.PropertyType);
                    else
                        il.Emit(OpCodes.Unbox_Any, propertyInfo.PropertyType);
                    il.Emit(OpCodes.Call, setMethod);
                }
            }

            il.Emit(OpCodes.Ret);

            return (GenericSetter)setter.CreateDelegate(typeof(GenericSetter));
        }

        internal static GenericGetter CreateGetField(Type type, FieldInfo fieldInfo)
        {
            DynamicMethod dynamicGet = new DynamicMethod("_cgf", typeof(object), new[] { typeof(object) }, type, true);

            ILGenerator il = dynamicGet.GetILGenerator();

            if (!type.IsClass) // structs
            {
                var lv = il.DeclareLocal(type);
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Unbox_Any, type);
                il.Emit(OpCodes.Stloc_0);
                il.Emit(OpCodes.Ldloca_S, lv);
                il.Emit(OpCodes.Ldfld, fieldInfo);
                if (fieldInfo.FieldType.IsValueType)
                    il.Emit(OpCodes.Box, fieldInfo.FieldType);
            }
            else
            {
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldfld, fieldInfo);
                if (fieldInfo.FieldType.IsValueType)
                    il.Emit(OpCodes.Box, fieldInfo.FieldType);
            }

            il.Emit(OpCodes.Ret);

            return (GenericGetter)dynamicGet.CreateDelegate(typeof(GenericGetter));
        }

        internal static GenericGetter? CreateGetMethod(Type type, PropertyInfo propertyInfo)
        {
            MethodInfo? getMethod = propertyInfo.GetGetMethod();
            if (getMethod == null)
                return null;

            DynamicMethod getter = new DynamicMethod("_cgm", typeof(object), new[] { typeof(object) }, type, true);

            ILGenerator il = getter.GetILGenerator();

            if (!type.IsClass) // structs
            {
                var lv = il.DeclareLocal(type);
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Unbox_Any, type);
                il.Emit(OpCodes.Stloc_0);
                il.Emit(OpCodes.Ldloca_S, lv);
                il.EmitCall(OpCodes.Call, getMethod, null);
                if (propertyInfo.PropertyType.IsValueType)
                    il.Emit(OpCodes.Box, propertyInfo.PropertyType);
            }
            else
            {
                if (!getMethod.IsStatic)
                {
                    il.Emit(OpCodes.Ldarg_0);
                    il.Emit(OpCodes.Castclass, propertyInfo.DeclaringType!);
                    il.EmitCall(OpCodes.Callvirt, getMethod, null);
                }
                else
                    il.Emit(OpCodes.Call, getMethod);

                if (propertyInfo.PropertyType.IsValueType)
                    il.Emit(OpCodes.Box, propertyInfo.PropertyType);
            }

            il.Emit(OpCodes.Ret);

            return (GenericGetter)getter.CreateDelegate(typeof(GenericGetter));
        }

        public Getters[] GetGetters(
            Type type, /*bool showReadOnlyProperties,*/
            IList<Type> ignoreAttributes
        )
        {
            Getters[]? val;
            if (_getterscache.TryGetValue(type, out val))
                return val!;

            var bf = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static;
            PropertyInfo[] props = type.GetProperties(bf);
            List<Getters> getters = new List<Getters>();
            foreach (PropertyInfo p in props)
            {
                bool readOnly = false;
                if (p.GetIndexParameters().Length > 0)
                { // Property is an indexer
                    continue;
                }
                if (!p.CanWrite) // && (showReadOnlyProperties == false))//|| isAnonymous == false))
                    readOnly = true; //continue;
                if (ignoreAttributes != null && ignoreAttributes.Any(ignoreAttr => p.IsDefined(ignoreAttr, false)))
                    continue;
                string? mName = null;
                var att = p.GetCustomAttributes(true);
                foreach (var at in att)
                {
                    if (at is DataMemberAttribute dm && !string.IsNullOrEmpty(dm.Name))
                    {
                        mName = dm.Name;
                    }
                }
                GenericGetter? g = CreateGetMethod(type, p);
                if (g != null)
                {
                    Getters getter = new Getters
                    {
                        Getter = g,
                        Name = p.Name,
                        MemberName = mName,
                        ReadOnly = readOnly,
                    };
                    BuildTypedGetter(ref getter, type, p.PropertyType, p.DeclaringType!, p.GetGetMethod(), null);
                    getters.Add(getter);
                }
            }

            FieldInfo[] fi = type.GetFields(bf);
            foreach (var f in fi)
            {
                bool readOnly = false;
                if (f.IsInitOnly) // && (showReadOnlyProperties == false))//|| isAnonymous == false))
                    readOnly = true; //continue;
                if (ignoreAttributes != null && ignoreAttributes.Any(ignoreAttr => f.IsDefined(ignoreAttr, false)))
                    continue;
                string? mName = null;
                var att = f.GetCustomAttributes(true);
                foreach (var at in att)
                {
                    if (at is DataMemberAttribute dm && !string.IsNullOrEmpty(dm.Name))
                    {
                        mName = dm.Name;
                    }
                }
                if (!f.IsLiteral)
                {
                    Getters getter = new Getters
                    {
                        Getter = CreateGetField(type, f),
                        Name = f.Name,
                        MemberName = mName,
                        ReadOnly = readOnly,
                    };
                    BuildTypedGetter(ref getter, type, f.FieldType, f.DeclaringType!, null, f);
                    getters.Add(getter);
                }
            }
            val = getters.ToArray();
            _getterscache.Add(type, val);
            return val;
        }
        #endregion

        internal void ResetPropertyCache()
        {
            _propertycache = new SafeDictionary<string, Dictionary<string, PropertyMetadata>>();
            _wirenamecache = new SafeDictionary<string, WireNameMap>();
        }

        internal void ClearReflectionCache()
        {
            _tyname = new SafeDictionary<Type, string>(10);
            _typecache = new SafeDictionary<string, Type?>(10);
            _constrcache = new SafeDictionary<Type, CreateObject>(10);
            _getterscache = new SafeDictionary<Type, Getters[]>(10);
            _propertycache = new SafeDictionary<string, Dictionary<string, PropertyMetadata>>(10);
            _wirenamecache = new SafeDictionary<string, WireNameMap>(10);
            _genericTypes = new SafeDictionary<Type, Type[]>(10);
            _genericTypeDef = new SafeDictionary<Type, Type>(10);
        }
    }
}
