using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Data;
using System.Globalization;
using System.IO;
using DuraIT.FastBinaryJson.Internal;
#if NET10_0_OR_GREATER
using System.Runtime.InteropServices;
#endif

namespace DuraIT.FastBinaryJson
{
    /*
     * On net10.0 the fixed-size writes go through stackalloc, strings are encoded straight into
     * the output, and the output is a pooled buffer rather than a MemoryStream. netstandard2.0 keeps upstream's
     * byte[] helpers for the fixed-size and string writes, but writes them into the same pooled buffer. Both write the same bytes in
     * the same (native) order - the golden files and the netstandard2.0 test project hold that,
     * so a divergence fails on one target rather than going unnoticed.
     *
     * The split lives in the small raw-write and Encode helpers at the bottom, not in the callers.
     */
    internal sealed class BjsonSerializer : IDisposable
    {
        private readonly PooledByteBuffer _output = new PooledByteBuffer();

        private int _typespointer;
        private readonly int _maxDepth;
        int _currentDepth;

        /*
         * The two per-call tables, kept per thread between calls. Growing them from empty cost four or five
         * resizes per call and, with their entry arrays, a quarter to a half of everything a small payload
         * allocated beyond its own bytes. A call takes the spare (leaving none, so a nested call from a
         * custom serializer builds its own), and Dispose clears and returns it. A table that grew past
         * MaxRetainedEntries is dropped instead of kept, so one huge call does not pin its memory to the
         * thread. Clear keeps insertion order for later additions, which is the order $types is written in.
         */
        private sealed class Tables
        {
            // By identity: $i means "this same instance". Default equality wrote a distinct object that
            // merely compared Equal as a reference to the first one, so an entity with Equals over its Id
            // lost every other member, and equal records or structs came back as one shared instance.
            public readonly Dictionary<object, int> Seen = new Dictionary<object, int>(ReferenceComparer.Instance);

            // Keyed by Type, not by assembly-qualified name: every object looks itself up here, and the name
            // key hashed around a hundred characters each time. The table is still written in the same
            // order, under the same ids, with the same names. Only two distinct types that share a name -
            // one assembly loaded twice - now get an id each instead of sharing one, and both still read
            // back as the type that name resolves to.
            public readonly Dictionary<Type, int> Types = new Dictionary<Type, int>();
        }

        private const int MaxRetainedEntries = 1024;

        [ThreadStatic]
        private static Tables? _spareTables;

        private Tables? _tables = TakeTables();
        private Dictionary<Type, int> GlobalTypes => _tables!.Types;
        private Dictionary<object, int> SeenObjects => _tables!.Seen;

        private static Tables TakeTables()
        {
            Tables tables = _spareTables ?? new Tables();
            _spareTables = null;
            return tables;
        }

        private readonly BjsonParameters _params;

        public void Dispose()
        {
            _output.Dispose();
            ReleasePooled();

            Tables? tables = _tables;
            _tables = null;
            if (tables != null)
                ReturnTables(tables);
        }

        private static void ReturnTables(Tables tables)
        {
            if (tables.Seen.Count > MaxRetainedEntries || tables.Types.Count > MaxRetainedEntries)
                return;

            tables.Seen.Clear();
            tables.Types.Clear();
            _spareTables = tables;
        }

        internal BjsonSerializer(BjsonParameters param)
        {
            _params = param;
            _maxDepth = param.SerializerMaxDepth;
        }

        internal byte[] ConvertToBjson(object obj)
        {
            // The pooled buffer is returned as soon as the bytes are copied out. Dispose releases it again,
            // which is a no-op, so a caller that forgets to dispose does not hold the buffer.
            // ToArray runs in the return expressions, before the finally.
            try
            {
                WriteValue(obj);

                // add $types
                if (_params.UsingGlobalTypes && GlobalTypes.Count > 0)
                {
                    var pointer = OutputLength;
                    WriteName("$types");
                    WriteColon();
                    WriteTypes(GlobalTypes);
                    PatchInt32(_typespointer, pointer);
                }

                return _output.ToArray();
            }
            finally
            {
                ReleasePooled();
            }
        }

        private void WriteTypes(Dictionary<Type, int> dic)
        {
            _output.WriteByte(Tokens.DocStart);

            bool pendingSeparator = false;

            foreach (var entry in dic)
            {
                if (pendingSeparator)
                    WriteComma();

                WritePair(entry.Value.ToString(CultureInfo.InvariantCulture), TypeReflector.Instance.GetTypeAssemblyName(entry.Key));

                pendingSeparator = true;
            }
            _output.WriteByte(Tokens.DocEnd);
        }

        private void WriteValue(object? obj)
        {
            if (obj == null || obj is DBNull)
                WriteNull();
            else if (obj is string text)
                WriteString(text);
            else if (obj is char character)
                WriteChar(character);
            else if (obj is Guid guid)
                WriteGuid(guid);
            else if (obj is bool flag)
                WriteBool(flag);
            else if (obj is int int32)
                WriteInt(int32);
            else if (obj is uint uint32)
                WriteUInt(uint32);
            else if (obj is long int64)
                WriteLong(int64);
            else if (obj is ulong uint64)
                WriteULong(uint64);
            else if (obj is decimal dec)
                WriteDecimal(dec);
            else
                WriteLessCommonValue(obj);
        }

        private void WriteLessCommonValue(object obj)
        {
            if (obj is sbyte sbyteValue)
                WriteSByte(sbyteValue);
            else if (obj is byte byteValue)
                WriteByte(byteValue);
            else if (obj is double dbl)
                WriteDouble(dbl);
            else if (obj is float sgl)
                WriteFloat(sgl);
            else if (obj is short int16)
                WriteShort(int16);
            else if (obj is ushort uint16)
                WriteUShort(uint16);
            else if (obj is DateTime dateTime)
                WriteDateTime(dateTime);
            else if (obj is TimeSpan timeSpan)
                WriteTimeSpan(timeSpan);
            else
                WriteByKind(obj);
        }

        /*
         * Everything that is not a primitive, dispatched on a classification cached per type. The order of the
         * checks lives in TypeReflector.ClassifyForWrite, not here.
         *
         * DateTimeOffset is classified AFTER the custom-type check rather than up with the primitives.
         * Registering a custom type was the only way to store a DateTimeOffset before its native form
         * existed, so anyone who stores one today has a registration and their stored data is a string.
         * Letting the native form win would change their bytes on the next write, and would break their
         * reads outright: a property whose declared type is registered is classified Custom, and that path
         * casts the parsed value to string.
         */
        private void WriteByKind(object obj)
        {
            switch (TypeReflector.Instance.GetWriteKind(obj.GetType()))
            {
                case WriteKind.ExpandoDictionary:
                    WriteStringDictionary((IDictionary<string, object>)obj);
                    break;
                case WriteKind.StringKeyedDictionary:
                    WriteStringDictionary((IDictionary)obj);
                    break;
                case WriteKind.Dictionary:
                    WriteDictionary((IDictionary)obj);
                    break;
                case WriteKind.DataSet:
                    WriteDataset((DataSet)obj);
                    break;
                case WriteKind.DataTable:
                    WriteDataTable((DataTable)obj);
                    break;
                case WriteKind.Bytes:
                    WriteBytes((byte[])obj);
                    break;
                case WriteKind.StringDictionary:
                    WriteSd((StringDictionary)obj);
                    break;
                case WriteKind.NameValueCollection:
                    WriteNv((NameValueCollection)obj);
                    break;
                case WriteKind.Array when _params.UseTypedArrays:
                    WriteTypedArray((ICollection)obj);
                    break;
                case WriteKind.Array:
                case WriteKind.Sequence:
                    WriteArray((IEnumerable)obj);
                    break;
                case WriteKind.Enum:
                    WriteEnum((Enum)obj);
                    break;
                case WriteKind.Custom:
                    WriteCustom(obj);
                    break;
                case WriteKind.DateTimeOffset:
                    WriteDateTimeOffset((DateTimeOffset)obj);
                    break;
                default:
                    WriteObject(obj);
                    break;
            }
        }

        private void WriteSByte(sbyte p)
        {
            _output.WriteByte(Tokens.SByte);
            _output.WriteByte(unchecked((byte)p));
        }

        private void WriteTimeSpan(TimeSpan obj)
        {
            _output.WriteByte(Tokens.TimeSpan);
            WriteInt64Raw(obj.Ticks);
        }

        private void WriteTypedArray(ICollection array)
        {
            bool pendingSeperator = false;
            bool token = true;
            var t = array.GetType();
            if (!t.IsGenericType) // != null) // non generic array
            {
                token = false;
                // array type name - byte[] on netstandard2.0, a PendingString on net10.0
                var b = Encode(TypeReflector.Instance.GetTypeAssemblyName(t.GetElementType()!), unicode: !_params.UseV14TypedArray);
                if (b.Length < 256)
                {
                    _output.WriteByte(Tokens.TypedArray);
                    _output.WriteByte((byte)b.Length);
                    WriteBytesRaw(b);
                }
                else
                {
                    _output.WriteByte(Tokens.TypedArrayLong);
                    WriteInt16Raw(unchecked((short)b.Length));
                    WriteBytesRaw(b);
                }
                // array count
                WriteInt32Raw(array.Count);
            }
            if (token)
                _output.WriteByte(Tokens.ArrayStart);

            foreach (object obj in array)
            {
                if (pendingSeperator)
                    WriteComma();

                WriteValue(obj);

                pendingSeperator = true;
            }
            _output.WriteByte(Tokens.ArrayEnd);
        }

        private void WriteNv(NameValueCollection nameValueCollection)
        {
            _output.WriteByte(Tokens.DocStart);

            bool pendingSeparator = false;

            foreach (string key in nameValueCollection)
            {
                if (pendingSeparator)
                    _output.WriteByte(Tokens.Comma);

                WritePair(key, nameValueCollection[key]);

                pendingSeparator = true;
            }
            _output.WriteByte(Tokens.DocEnd);
        }

        private void WriteSd(StringDictionary stringDictionary)
        {
            _output.WriteByte(Tokens.DocStart);

            bool pendingSeparator = false;

            foreach (DictionaryEntry entry in stringDictionary)
            {
                if (pendingSeparator)
                    _output.WriteByte(Tokens.Comma);

                WritePair((string)entry.Key, entry.Value);

                pendingSeparator = true;
            }
            _output.WriteByte(Tokens.DocEnd);
        }

        private void WriteUShort(ushort p)
        {
            _output.WriteByte(Tokens.UInt16);
            WriteInt16Raw(unchecked((short)p));
        }

        private void WriteShort(short p)
        {
            _output.WriteByte(Tokens.Int16);
            WriteInt16Raw(p);
        }

        private void WriteFloat(float p)
        {
            _output.WriteByte(Tokens.Single);
#if NET10_0_OR_GREATER
            WriteInt32Raw(BitConverter.SingleToInt32Bits(p));
#else
            byte[] b = BitConverter.GetBytes(p);
            _output.Write(b);
#endif
        }

        private void WriteDouble(double p)
        {
            _output.WriteByte(Tokens.Double);
#if NET10_0_OR_GREATER
            WriteInt64Raw(BitConverter.DoubleToInt64Bits(p));
#else
            var b = BitConverter.GetBytes(p);
            _output.Write(b);
#endif
        }

        private void WriteByte(byte p)
        {
            _output.WriteByte(Tokens.Byte);
            _output.WriteByte(p);
        }

        private void WriteDecimal(decimal p)
        {
            _output.WriteByte(Tokens.Decimal);
#if NET10_0_OR_GREATER
            Span<int> b = stackalloc int[4];
            _ = decimal.GetBits(p, b);
#else
            var b = decimal.GetBits(p);
#endif
            foreach (var c in b)
                WriteInt32Raw(c);
        }

        private void WriteULong(ulong p)
        {
            _output.WriteByte(Tokens.UInt64);
            WriteInt64Raw(unchecked((long)p));
        }

        private void WriteUInt(uint p)
        {
            _output.WriteByte(Tokens.UInt32);
            WriteInt32Raw(unchecked((int)p));
        }

        private void WriteLong(long p)
        {
            _output.WriteByte(Tokens.Int64);
            WriteInt64Raw(p);
        }

        private void WriteChar(char p)
        {
            _output.WriteByte(Tokens.Char);
            WriteInt16Raw(unchecked((short)p));
        }

        private void WriteBytes(byte[] p)
        {
            _output.WriteByte(Tokens.ByteArray);
            WriteInt32Raw(p.Length);
            WriteBytesRaw(p);
        }

        private void WriteBool(bool p)
        {
            if (p)
                _output.WriteByte(Tokens.True);
            else
                _output.WriteByte(Tokens.False);
        }

        private void WriteNull()
        {
            _output.WriteByte(Tokens.Null);
        }

        private void WriteCustom(object obj)
        {
            TypeReflector.Instance.TryGetCustomSerializer(obj.GetType(), out CustomTypeSerializer? s);
            WriteString(s!(obj));
        }

        private void WriteColon()
        {
            _output.WriteByte(Tokens.Colon);
        }

        private void WriteComma()
        {
            _output.WriteByte(Tokens.Comma);
        }

        private void WriteEnum(Enum e)
        {
            WriteString(e.ToString());
        }

        private void WriteInt(int i)
        {
            _output.WriteByte(Tokens.Int32);
            WriteInt32Raw(i);
        }

        private void WriteGuid(Guid g)
        {
            _output.WriteByte(Tokens.Guid);
#if NET10_0_OR_GREATER
            Span<byte> b = stackalloc byte[16];
            g.TryWriteBytes(b);
            _output.Write(b);
#else
            _output.Write(g.ToByteArray());
#endif
        }

        private void WriteDateTime(DateTime dateTime)
        {
            DateTime dt = dateTime;
            if (_params.UseUtcDateTime)
                dt = dateTime.ToUniversalTime();

            _output.WriteByte(Tokens.DateTime);
            WriteInt64Raw(dt.Ticks);
        }

        /// <summary>
        /// Writes a DateTimeOffset as raw clock ticks plus its offset in whole minutes.
        /// </summary>
        /// <remarks>
        /// Tokens.DateTimeOffset was declared by upstream and never written, so this fills in a dead
        /// token and no existing stream is affected.
        ///
        /// UseUtcDateTime is not consulted. It exists to decide which clock a DateTime means, and a
        /// DateTimeOffset already carries that answer - normalizing it would discard the offset the
        /// caller chose. The offset is signed and written as two bytes, which covers the whole
        /// permitted range of -14:00 to +14:00 with room to spare.
        /// </remarks>
        private void WriteDateTimeOffset(DateTimeOffset value)
        {
            _output.WriteByte(Tokens.DateTimeOffset);
            WriteInt64Raw(value.Ticks);
            WriteInt16Raw(unchecked((short)(int)value.Offset.TotalMinutes));
        }

        private static DatasetSchema? GetSchema(DataTable? ds)
        {
            if (ds == null)
                return null;

            DatasetSchema m = new DatasetSchema();
            m.Info = new List<string>();
            m.Name = ds.TableName;

            foreach (DataColumn c in ds.Columns)
            {
                m.Info.Add(ds.TableName);
                m.Info.Add(c.ColumnName);
                m.Info.Add(c.DataType.ToString());
            }
            // FEATURE : serialize relations and constraints here

            return m;
        }

        private static DatasetSchema? GetSchema(DataSet? ds)
        {
            if (ds == null)
                return null;

            DatasetSchema m = new DatasetSchema();
            m.Info = new List<string>();
            m.Name = ds.DataSetName;

            foreach (DataTable t in ds.Tables)
            {
                foreach (DataColumn c in t.Columns)
                {
                    m.Info.Add(t.TableName);
                    m.Info.Add(c.ColumnName);
                    m.Info.Add(c.DataType.ToString());
                }
            }
            // FEATURE : serialize relations and constraints here

            return m;
        }

        private static string GetXmlSchema(DataTable dt)
        {
            using (var writer = new StringWriter())
            {
                dt.WriteXmlSchema(writer);
                return writer.ToString();
            }
        }

        private void WriteDataset(DataSet ds)
        {
            _output.WriteByte(Tokens.DocStart);
            WritePair(WireKeys.Schema, _params.UseOptimizedDatasetSchema ? GetSchema(ds) : ds.GetXmlSchema());
            WriteComma();
            bool tablesep = false;
            foreach (DataTable table in ds.Tables)
            {
                if (tablesep)
                    WriteComma();
                tablesep = true;
                WriteDataTableData(table);
            }
            // end dataset
            _output.WriteByte(Tokens.DocEnd);
        }

        private void WriteDataTableData(DataTable table)
        {
            WriteName(table.TableName);
            WriteColon();
            _output.WriteByte(Tokens.ArrayStart);
            DataColumnCollection cols = table.Columns;
            bool rowseparator = false;
            foreach (DataRow row in table.Rows)
            {
                if (rowseparator)
                    WriteComma();
                rowseparator = true;
                _output.WriteByte(Tokens.ArrayStart);

                bool pendingSeperator = false;
                foreach (DataColumn column in cols)
                {
                    if (pendingSeperator)
                        WriteComma();
                    WriteValue(row[column]);
                    pendingSeperator = true;
                }
                _output.WriteByte(Tokens.ArrayEnd);
            }

            _output.WriteByte(Tokens.ArrayEnd);
        }

        void WriteDataTable(DataTable dt)
        {
            _output.WriteByte(Tokens.DocStart);
            this.WritePair(WireKeys.Schema, _params.UseOptimizedDatasetSchema ? GetSchema(dt) : GetXmlSchema(dt));
            WriteComma();

            WriteDataTableData(dt);

            // end datatable
            _output.WriteByte(Tokens.DocEnd);
        }

        bool _typesWritten;

        private void WriteObject(object obj)
        {
            if (TryWriteReference(obj))
                return;

            WriteObjectStart();
            _currentDepth++;
            if (_currentDepth > _maxDepth)
                throw new BjsonException("Serializer encountered maximum depth of " + _maxDepth);

            Type t = obj.GetType();
            bool append = false;
            if (_params.UseExtensions)
            {
                if (!_params.UsingGlobalTypes)
                    WritePairFast(WireKeys.Type, TypeReflector.Instance.GetTypeAssemblyName(t));
                else
                    WritePairFast(WireKeys.Type, GetGlobalTypeId(t));
                append = true;
            }

            Getters[] g = TypeReflector.Instance.GetGetters(
                t, /*_params.ShowReadOnlyProperties,*/
                _params.IgnoreAttributes
            );
            WriteMembers(g, obj, append);
            _output.WriteByte(Tokens.DocEnd);
            _currentDepth--;
        }

        /// <summary>
        /// Registers the object, and writes a $i reference instead when it was seen before and is nested.
        /// </summary>
        private bool TryWriteReference(object obj)
        {
            int i;
            if (!SeenObjects.TryGetValue(obj, out i))
            {
                SeenObjects.Add(obj, SeenObjects.Count + 1);
                return false;
            }

            if (_currentDepth <= 0)
                return false;

            _output.WriteByte(Tokens.DocStart);
            WriteName("$i");
            WriteColon();
            WriteValue(i);
            _output.WriteByte(Tokens.DocEnd);
            return true;
        }

        private void WriteObjectStart()
        {
            if (_params.UsingGlobalTypes && !_typesWritten)
            {
                _output.WriteByte(Tokens.DocStart);
                // write pointer to $types position
                _output.WriteByte(Tokens.TypesPointer);
                _typespointer = OutputLength; // place holder
                WriteInt32Raw(0); // zero pointer for now
                _typesWritten = true;
                return;
            }

            _output.WriteByte(Tokens.DocStart);
        }

        private void WriteMembers(Getters[] g, object obj, bool append)
        {
            int c = g.Length;
            for (int ii = 0; ii < c; ii++)
            {
                ref Getters p = ref g[ii];
                if (p.TypedGetter != null && TypedGetters)
                {
                    // A primitive is never null, so it is always written - as below, minus the box.
                    if (append)
                        WriteComma();
                    WriteBytesRaw(MemberKey(ref p));
                    WriteTyped(ref p, obj);
                    append = true;
                    continue;
                }

                var o = p.Getter(obj);
                if (_params.SerializeNulls || (o != null && o is not DBNull))
                {
                    if (append)
                        WriteComma();
                    // Name and colon in one copy; WritePair's own null check cannot fire here.
                    WriteBytesRaw(MemberKey(ref p));
                    WriteValue(o);
                    append = true;
                }
            }
        }

        /// <summary>
        /// Read primitive members through their typed getters instead of the boxing one.
        /// </summary>
        /// <remarks>
        /// Off only in tests, to write with the boxing path as the reference.
        /// </remarks>
        internal bool TypedGetters { get; set; } = true;

        /// <summary>
        /// Writes a primitive member's value without boxing it, with the writer WriteValue picks for it.
        /// </summary>
        /// <exception cref="InvalidOperationException">If a getter carries a token with no writer here.</exception>
        private void WriteTyped(ref Getters p, object obj)
        {
            Delegate getter = p.TypedGetter!;
            switch (p.TypedToken)
            {
                case Tokens.Int32:
                    WriteInt(((TypeReflector.TypedGetter<int>)getter)(obj));
                    break;
                case Tokens.Int64:
                    WriteLong(((TypeReflector.TypedGetter<long>)getter)(obj));
                    break;
                case Tokens.True:
                    WriteBool(((TypeReflector.TypedGetter<bool>)getter)(obj));
                    break;
                case Tokens.DateTime:
                    WriteDateTime(((TypeReflector.TypedGetter<DateTime>)getter)(obj));
                    break;
                case Tokens.Guid:
                    WriteGuid(((TypeReflector.TypedGetter<Guid>)getter)(obj));
                    break;
                case Tokens.Double:
                    WriteDouble(((TypeReflector.TypedGetter<double>)getter)(obj));
                    break;
                case Tokens.Single:
                    WriteFloat(((TypeReflector.TypedGetter<float>)getter)(obj));
                    break;
                case Tokens.Decimal:
                    WriteDecimal(((TypeReflector.TypedGetter<decimal>)getter)(obj));
                    break;
                case Tokens.Int16:
                    WriteShort(((TypeReflector.TypedGetter<short>)getter)(obj));
                    break;
                case Tokens.UInt16:
                    WriteUShort(((TypeReflector.TypedGetter<ushort>)getter)(obj));
                    break;
                case Tokens.UInt32:
                    WriteUInt(((TypeReflector.TypedGetter<uint>)getter)(obj));
                    break;
                case Tokens.UInt64:
                    WriteULong(((TypeReflector.TypedGetter<ulong>)getter)(obj));
                    break;
                case Tokens.Byte:
                    WriteByte(((TypeReflector.TypedGetter<byte>)getter)(obj));
                    break;
                case Tokens.SByte:
                    WriteSByte(((TypeReflector.TypedGetter<sbyte>)getter)(obj));
                    break;
                case Tokens.Char:
                    WriteChar(((TypeReflector.TypedGetter<char>)getter)(obj));
                    break;
                case Tokens.TimeSpan:
                    WriteTimeSpan(((TypeReflector.TypedGetter<TimeSpan>)getter)(obj));
                    break;
                default:
                    throw new InvalidOperationException("No typed writer for token " + p.TypedToken + ".");
            }
        }

        /// <summary>
        /// A member's name and the colon after it, as written in the current encoding.
        /// </summary>
        /// <remarks>
        /// Encoded once per member and encoding and kept on its getter, instead of measuring and
        /// encoding the name again for every object. The DataMember name when there is one - the
        /// reader has expected it since upstream v1.4.23, but no writer ever wrote it until this fork.
        /// </remarks>
        private byte[] MemberKey(ref Getters p)
        {
            if (_params.UseUnicodeStrings)
                return p.KeyUtf16 ??= EncodeKey(p.MemberName ?? p.Name);

            return p.KeyUtf8 ??= EncodeKey(p.MemberName ?? p.Name);
        }

        // Written by WriteName and WriteColon themselves, so the cached bytes cannot differ from theirs.
        private byte[] EncodeKey(string name)
        {
            using (BjsonSerializer scratch = new BjsonSerializer(_params))
            {
                scratch.WriteName(name);
                scratch.WriteColon();
                return scratch._output.ToArray();
            }
        }

        // The $types id for t, assigned in first-use order.
        private string GetGlobalTypeId(Type t)
        {
            if (!GlobalTypes.TryGetValue(t, out int dt))
            {
                dt = GlobalTypes.Count + 1;
                GlobalTypes.Add(t, dt);
            }

            return dt.ToString(CultureInfo.InvariantCulture);
        }

        private void WritePairFast(string name, string value)
        {
            WriteName(name);

            WriteColon();

            WriteString(value);
        }

        private void WritePair(string name, object? value)
        {
            if (!_params.SerializeNulls && (value == null || value is DBNull))
                return;
            WriteName(name);

            WriteColon();

            WriteValue(value);
        }

        private void WriteArray(IEnumerable array)
        {
            _output.WriteByte(Tokens.ArrayStart);

            bool pendingSeperator = false;

            foreach (object obj in array)
            {
                if (pendingSeperator)
                    WriteComma();

                WriteValue(obj);

                pendingSeperator = true;
            }
            _output.WriteByte(Tokens.ArrayEnd);
        }

        private void WriteStringDictionary(IDictionary dic)
        {
            _output.WriteByte(Tokens.DocStart);

            bool pendingSeparator = false;

            foreach (DictionaryEntry entry in dic)
            {
                if (pendingSeparator)
                    WriteComma();

                WritePair((string)entry.Key, entry.Value);

                pendingSeparator = true;
            }
            _output.WriteByte(Tokens.DocEnd);
        }

        private void WriteStringDictionary(IDictionary<string, object> dic)
        {
            _output.WriteByte(Tokens.DocStart);

            bool pendingSeparator = false;

            foreach (KeyValuePair<string, object> entry in dic)
            {
                if (pendingSeparator)
                    WriteComma();

                WritePair(entry.Key, entry.Value);

                pendingSeparator = true;
            }
            _output.WriteByte(Tokens.DocEnd);
        }

        private void WriteDictionary(IDictionary dic)
        {
            _output.WriteByte(Tokens.ArrayStart);

            bool pendingSeparator = false;

            foreach (DictionaryEntry entry in dic)
            {
                if (pendingSeparator)
                    WriteComma();
                _output.WriteByte(Tokens.DocStart);
                WritePair("k", entry.Key);
                WriteComma();
                WritePair("v", entry.Value);
                _output.WriteByte(Tokens.DocEnd);

                pendingSeparator = true;
            }
            _output.WriteByte(Tokens.ArrayEnd);
        }

        /// <summary>
        /// Writes an encoded name, choosing the single-byte or the four-byte length form.
        /// </summary>
        /// <remarks>
        /// This used to put the length into one byte and then write `b.Length % 256` bytes, so both
        /// wrapped and any name of 256 encoded bytes or more was silently truncated. The short form
        /// is kept for everything below that threshold, unchanged, which is what leaves every name
        /// that already worked byte-identical.
        /// </remarks>
        private void WriteName(string s)
        {
            bool unicode = _params.UseUnicodeStrings;
            // byte[] on netstandard2.0, a PendingString on net10.0
            var b = Encode(s, unicode);
            if (b.Length < 256)
            {
                _output.WriteByte(unicode ? Tokens.NameUtf16 : Tokens.Name);
                _output.WriteByte((byte)b.Length);
            }
            else
            {
                _output.WriteByte(unicode ? Tokens.NameUtf16Long : Tokens.NameLong);
                WriteInt32Raw(b.Length);
            }

            WriteBytesRaw(b);
        }

        private void WriteString(string s)
        {
            bool unicode = _params.UseUnicodeStrings;
            _output.WriteByte(unicode ? Tokens.Utf16String : Tokens.Utf8String);
            var b = Encode(s, unicode);
            WriteInt32Raw(b.Length);
            WriteBytesRaw(b);
        }

        #region Raw writes

        /*
         * Native byte order on both targets, exactly as Helper.GetBytes wrote it. Not
         * BinaryPrimitives.WriteXxxLittleEndian: that would only differ on big-endian hardware, but
         * byte-identical to upstream is the rule, not "identical where it happens to matter".
         */
        private void WriteInt16Raw(short value)
        {
#if NET10_0_OR_GREATER
            Span<byte> buffer = stackalloc byte[sizeof(short)];
            MemoryMarshal.Write(buffer, in value);
            _output.Write(buffer);
#else
            _output.Write(new ReadOnlySpan<byte>(Helper.GetBytes(value, false), 0, 2));
#endif
        }

        private void WriteInt32Raw(int value)
        {
#if NET10_0_OR_GREATER
            Span<byte> buffer = stackalloc byte[sizeof(int)];
            MemoryMarshal.Write(buffer, in value);
            _output.Write(buffer);
#else
            _output.Write(new ReadOnlySpan<byte>(Helper.GetBytes(value, false), 0, 4));
#endif
        }

        private void WriteInt64Raw(long value)
        {
#if NET10_0_OR_GREATER
            Span<byte> buffer = stackalloc byte[sizeof(long)];
            MemoryMarshal.Write(buffer, in value);
            _output.Write(buffer);
#else
            _output.Write(new ReadOnlySpan<byte>(Helper.GetBytes(value, false), 0, 8));
#endif
        }

        // Overwrites four bytes written earlier - the $types pointer placeholder.
        private void PatchInt32(int position, int value)
        {
            _output.WriteInt32At(position, value);
        }

#if NET10_0_OR_GREATER
        /// <summary>
        /// A string whose encoded length is known but whose bytes are not produced yet.
        /// </summary>
        /// <remarks>
        /// Every caller writes a length header before the bytes, so the length has to come first.
        /// Carrying it separately lets <c>WriteBytesRaw</c> encode straight into the output
        /// afterwards, instead of encoding into a scratch buffer and copying.
        /// </remarks>
        private readonly record struct PendingString(string Value, bool Unicode, int Length);

        // Only measures; WriteBytesRaw produces the bytes.
        private static PendingString Encode(string s, bool unicode) =>
            new PendingString(s, unicode, unicode ? s.Length * sizeof(char) : TypeReflector.Utf8GetByteCount(s));

        /// <summary>
        /// Writes the string's bytes into the output without an intermediate copy.
        /// </summary>
        /// <exception cref="InvalidOperationException">If the encoder wrote another length than it counted.</exception>
        private void WriteBytesRaw(PendingString pending)
        {
            // The same bytes UnicodeGetBytes copied out of the string, without the copy.
            if (pending.Unicode)
            {
                _output.Write(MemoryMarshal.AsBytes(pending.Value.AsSpan()));
                return;
            }

            // Counted and encoded by the same encoder instance, so the two cannot disagree; checked
            // anyway, because the header carrying the count is already written.
            int written = TypeReflector.Utf8GetBytes(pending.Value, _output.GetSpan(pending.Length));
            if (written != pending.Length)
                throw new InvalidOperationException($"UTF-8 encoder wrote {written} bytes after counting {pending.Length}.");

            _output.Advance(written);
        }

        private void WriteBytesRaw(ReadOnlySpan<byte> bytes) => _output.Write(bytes);

        // Idempotent: called from ConvertToBjson's finally and again from Dispose.
        private void ReleasePooled() => _output.Dispose();

        private int OutputLength => _output.Length;
#else
        private static byte[] Encode(string s, bool unicode) => unicode ? TypeReflector.UnicodeGetBytes(s) : TypeReflector.Utf8GetBytes(s);

        private void WriteBytesRaw(byte[] bytes) => _output.Write(bytes);

        // Idempotent: called from ConvertToBjson's finally and again from Dispose.
        private void ReleasePooled() => _output.Dispose();

        private int OutputLength => _output.Length;
#endif

        #endregion
    }
}
