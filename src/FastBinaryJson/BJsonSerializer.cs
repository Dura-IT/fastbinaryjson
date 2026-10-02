using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Collections.Specialized;
using DuraIT.FastBinaryJson.Internal;
#if NET10_0_OR_GREATER
using System.Runtime.InteropServices;
#endif

namespace DuraIT.FastBinaryJson
{
    /*
     * On net10.0 the fixed-size writes go through stackalloc, strings are encoded straight into
     * the output, and the output is a pooled buffer rather than a MemoryStream.
     * netstandard2.0 keeps upstream's byte[] and MemoryStream path. Both write the same bytes in
     * the same (native) order - the golden files and the netstandard2.0 test project hold that,
     * so a divergence fails on one target rather than going unnoticed.
     *
     * The split lives in the small raw-write and Encode helpers at the bottom, not in the callers.
     */
    internal sealed class BJSONSerializer : IDisposable
    {
#if NET10_0_OR_GREATER
        private readonly PooledByteBuffer _output = new PooledByteBuffer();
#else
        private readonly MemoryStream _output = new MemoryStream();
#endif
        //private MemoryStream _before = new MemoryStream();
        private int _typespointer = 0;
        private int _MAX_DEPTH = 20;
        int _current_depth = 0;
        private Dictionary<string, int> _globalTypes = new Dictionary<string, int>();
        // Created on first use: a root list or dictionary turns global types off and never needs it.
        private Dictionary<Type, string>? _globalTypeIds;
        /*
         * By identity: $i means "this same instance". Default equality wrote a distinct object that
         * merely compared Equal as a reference to the first one, so an entity with Equals over its Id
         * lost every other member, and equal records or structs came back as one shared instance.
         */
        private Dictionary<object, int> _cirobj = new Dictionary<object, int>(ReferenceComparer.Instance);
        private BJSONParameters _params;

        private void Dispose(bool disposing)
        {
            if (disposing)
            {
                // dispose managed resources
                _output.Dispose();
                //_before.Close();
                ReleasePooled();
            }
            // free native resources
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        internal BJSONSerializer(BJSONParameters param)
        {
            _params = param;
            _MAX_DEPTH = param.SerializerMaxDepth;
        }

        internal byte[] ConvertToBJSON(object obj)
        {
            // The caller does not dispose this instance, so pooled buffers are returned here.
            // ToArray runs in the return expressions, before the finally.
            try
            {
                WriteValue(obj);

                // add $types
                if (_params.UsingGlobalTypes && _globalTypes != null && _globalTypes.Count > 0)
                {
                    var pointer = (int)_output.Length;
                    WriteName("$types");
                    WriteColon();
                    WriteTypes(_globalTypes);
                    //var i = _output.Length;
                    PatchInt32(_typespointer, pointer);

                    return _output.ToArray();
                }

                return _output.ToArray();
            }
            finally
            {
                ReleasePooled();
            }
        }

        private void WriteTypes(Dictionary<string, int> dic)
        {
            _output.WriteByte(TOKENS.DOC_START);

            bool pendingSeparator = false;

            foreach (var entry in dic)
            {
                if (pendingSeparator) WriteComma();

                WritePair(entry.Value.ToString(), entry.Key);

                pendingSeparator = true;
            }
            _output.WriteByte(TOKENS.DOC_END);
        }

        private void WriteValue(object? obj)
        {
            if (obj == null || obj is DBNull)
                WriteNull();

            else if (obj is string)
                WriteString((string)obj);

            else if (obj is char)
                WriteChar((char)obj);

            else if (obj is Guid)
                WriteGuid((Guid)obj);

            else if (obj is bool)
                WriteBool((bool)obj);

            else if (obj is int)
                WriteInt((int)obj);

            else if (obj is uint)
                WriteUInt((uint)obj);

            else if (obj is long)
                WriteLong((long)obj);

            else if (obj is ulong)
                WriteULong((ulong)obj);

            else if (obj is decimal)
                WriteDecimal((decimal)obj);

            else if (obj is sbyte)
                WriteSByte((sbyte)obj);

            else if (obj is byte)
                WriteByte((byte)obj);

            else if (obj is double)
                WriteDouble((double)obj);

            else if (obj is float)
                WriteFloat((float)obj);

            else if (obj is short)
                WriteShort((short)obj);

            else if (obj is ushort)
                WriteUShort((ushort)obj);

            else if (obj is DateTime)
                WriteDateTime((DateTime)obj);

            else if (obj is TimeSpan)
                WriteTimeSpan((TimeSpan)obj);
            else if (obj is System.Dynamic.ExpandoObject)
                WriteStringDictionary((IDictionary<string, object>)obj);

            else if (obj is IDictionary && obj.GetType().IsGenericType && obj.GetType().GetGenericArguments()[0] == typeof(string))
                WriteStringDictionary((IDictionary)obj);

            else if (obj is IDictionary)
                WriteDictionary((IDictionary)obj);
            else if (obj is DataSet)
                WriteDataset((DataSet)obj);

            else if (obj is DataTable)
                WriteDataTable((DataTable)obj);
            else if (obj is byte[])
                WriteBytes((byte[])obj);

            else if (obj is StringDictionary)
                WriteSD((StringDictionary)obj);

            else if (obj is NameValueCollection)
                WriteNV((NameValueCollection)obj);

            else if (_params.UseTypedArrays && obj is Array)
                WriteTypedArray((ICollection)obj);

            else if (obj is IEnumerable)
                WriteArray((IEnumerable)obj);

            else if (obj is Enum)
                WriteEnum((Enum)obj);

            else if (Reflection.Instance.IsTypeRegistered(obj.GetType()))
                WriteCustom(obj);

            /*
             * Deliberately AFTER the custom-type check rather than up with the other primitives.
             *
             * Registering a custom type was the only way to store a DateTimeOffset before this
             * branch existed, so anyone who stores one today has a registration and their stored
             * data is a string. Letting the native form win would change their bytes on the next
             * write, and would break their reads outright: a property whose declared type is
             * registered is classified Custom, and that path casts the parsed value to string.
             */
            else if (obj is DateTimeOffset)
                WriteDateTimeOffset((DateTimeOffset)obj);

            else
                WriteObject(obj);
        }

        private void WriteSByte(sbyte p)
        {
            _output.WriteByte(TOKENS.SBYTE);
            _output.WriteByte(unchecked((byte)p));
        }

        private void WriteTimeSpan(TimeSpan obj)
        {
            _output.WriteByte(TOKENS.TIMESPAN);
            WriteInt64Raw(obj.Ticks);
        }

        private void WriteTypedArray(ICollection array)
        {
            bool pendingSeperator = false;
            bool token = true;
            var t = array.GetType();
            if (t.IsGenericType == false)// != null) // non generic array
            {
                //if (t.GetElementType().IsClass)
                {
                    token = false;
                    // array type name - byte[] on netstandard2.0, a PendingString on net10.0
                    var b = Encode(Reflection.Instance.GetTypeAssemblyName(t.GetElementType()!), unicode: !_params.v1_4TypedArray);
                    if (b.Length < 256)
                    {
                        _output.WriteByte(TOKENS.ARRAY_TYPED);
                        _output.WriteByte((byte)b.Length);
                        WriteBytesRaw(b);
                    }
                    else
                    {
                        _output.WriteByte(TOKENS.ARRAY_TYPED_LONG);
                        WriteInt16Raw(unchecked((short)b.Length));
                        WriteBytesRaw(b);
                    }
                    // array count
                    WriteInt32Raw(array.Count);
                }
            }
            if (token)
                _output.WriteByte(TOKENS.ARRAY_START);

            foreach (object obj in array)
            {
                if (pendingSeperator) WriteComma();

                WriteValue(obj);

                pendingSeperator = true;
            }
            _output.WriteByte(TOKENS.ARRAY_END);
        }

        private void WriteNV(NameValueCollection nameValueCollection)
        {
            _output.WriteByte(TOKENS.DOC_START);

            bool pendingSeparator = false;

            foreach (string key in nameValueCollection)
            {
                if (pendingSeparator) _output.WriteByte(TOKENS.COMMA);

                WritePair(key, nameValueCollection[key]);

                pendingSeparator = true;
            }
            _output.WriteByte(TOKENS.DOC_END);
        }

        private void WriteSD(StringDictionary stringDictionary)
        {
            _output.WriteByte(TOKENS.DOC_START);

            bool pendingSeparator = false;

            foreach (DictionaryEntry entry in stringDictionary)
            {
                if (pendingSeparator) _output.WriteByte(TOKENS.COMMA);

                WritePair((string)entry.Key, entry.Value);

                pendingSeparator = true;
            }
            _output.WriteByte(TOKENS.DOC_END);
        }

        private void WriteUShort(ushort p)
        {
            _output.WriteByte(TOKENS.USHORT);
            WriteInt16Raw(unchecked((short)p));
        }

        private void WriteShort(short p)
        {
            _output.WriteByte(TOKENS.SHORT);
            WriteInt16Raw(p);
        }

        private void WriteFloat(float p)
        {
            _output.WriteByte(TOKENS.FLOAT);
#if NET10_0_OR_GREATER
            WriteInt32Raw(BitConverter.SingleToInt32Bits(p));
#else
            byte[] b = BitConverter.GetBytes(p);
            _output.Write(b, 0, b.Length);
#endif
        }

        private void WriteDouble(double p)
        {
            _output.WriteByte(TOKENS.DOUBLE);
#if NET10_0_OR_GREATER
            WriteInt64Raw(BitConverter.DoubleToInt64Bits(p));
#else
            var b = BitConverter.GetBytes(p);
            _output.Write(b, 0, b.Length);
#endif
        }

        private void WriteByte(byte p)
        {
            _output.WriteByte(TOKENS.BYTE);
            _output.WriteByte(p);
        }

        private void WriteDecimal(decimal p)
        {
            _output.WriteByte(TOKENS.DECIMAL);
#if NET10_0_OR_GREATER
            Span<int> b = stackalloc int[4];
            decimal.GetBits(p, b);
#else
            var b = decimal.GetBits(p);
#endif
            foreach (var c in b)
                WriteInt32Raw(c);
        }

        private void WriteULong(ulong p)
        {
            _output.WriteByte(TOKENS.ULONG);
            WriteInt64Raw(unchecked((long)p));
        }

        private void WriteUInt(uint p)
        {
            _output.WriteByte(TOKENS.UINT);
            WriteInt32Raw(unchecked((int)p));
        }

        private void WriteLong(long p)
        {
            _output.WriteByte(TOKENS.LONG);
            WriteInt64Raw(p);
        }

        private void WriteChar(char p)
        {
            _output.WriteByte(TOKENS.CHAR);
            WriteInt16Raw(unchecked((short)p));
        }

        private void WriteBytes(byte[] p)
        {
            _output.WriteByte(TOKENS.BYTEARRAY);
            WriteInt32Raw(p.Length);
            WriteBytesRaw(p);
        }

        private void WriteBool(bool p)
        {
            if (p)
                _output.WriteByte(TOKENS.TRUE);
            else
                _output.WriteByte(TOKENS.FALSE);
        }

        private void WriteNull()
        {
            _output.WriteByte(TOKENS.NULL);
        }


        private void WriteCustom(object obj)
        {
            Reflection.Instance.TryGetCustomSerializer(obj.GetType(), out Reflection.Serialize? s);
            WriteString(s!(obj));
        }

        private void WriteColon()
        {
            _output.WriteByte(TOKENS.COLON);
        }

        private void WriteComma()
        {
            _output.WriteByte(TOKENS.COMMA);
        }

        private void WriteEnum(Enum e)
        {
            WriteString(e.ToString());
        }

        private void WriteInt(int i)
        {
            //if (_params.OptimizeSize)
            //{
            //    if (i < 256)
            //    {
            //        WriteByte((byte)i);
            //        return;
            //    }
            //    else if (i < 65536)
            //    {
            //        WriteUShort((ushort)i);
            //        return;
            //    }
            //}
            _output.WriteByte(TOKENS.INT);
            WriteInt32Raw(i);
        }

        private void WriteGuid(Guid g)
        {
            _output.WriteByte(TOKENS.GUID);
#if NET10_0_OR_GREATER
            Span<byte> b = stackalloc byte[16];
            g.TryWriteBytes(b);
            _output.Write(b);
#else
            _output.Write(g.ToByteArray(), 0, 16);
#endif
        }

        private void WriteDateTime(DateTime dateTime)
        {
            DateTime dt = dateTime;
            if (_params.UseUTCDateTime)
                dt = dateTime.ToUniversalTime();

            _output.WriteByte(TOKENS.DATETIME);
            WriteInt64Raw(dt.Ticks);
        }

        /// <summary>
        /// Writes a DateTimeOffset as raw clock ticks plus its offset in whole minutes.
        /// </summary>
        /// <remarks>
        /// TOKENS.DATETIMEOFFSET was declared by upstream and never written, so this fills in a dead
        /// token and no existing stream is affected.
        ///
        /// UseUTCDateTime is not consulted. It exists to decide which clock a DateTime means, and a
        /// DateTimeOffset already carries that answer - normalizing it would discard the offset the
        /// caller chose. The offset is signed and written as two bytes, which covers the whole
        /// permitted range of -14:00 to +14:00 with room to spare.
        /// </remarks>
        private void WriteDateTimeOffset(DateTimeOffset value)
        {
            _output.WriteByte(TOKENS.DATETIMEOFFSET);
            WriteInt64Raw(value.Ticks);
            WriteInt16Raw(unchecked((short)(int)value.Offset.TotalMinutes));
        }

        private DatasetSchema? GetSchema(DataTable? ds)
        {
            if (ds == null) return null;

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

        private DatasetSchema? GetSchema(DataSet? ds)
        {
            if (ds == null) return null;

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

        private string GetXmlSchema(DataTable dt)
        {
            using (var writer = new StringWriter())
            {
                dt.WriteXmlSchema(writer);
                return dt.ToString();
            }
        }

        private void WriteDataset(DataSet ds)
        {
            _output.WriteByte(TOKENS.DOC_START);
            {
                WritePair("$schema", _params.UseOptimizedDatasetSchema ? (object?)GetSchema(ds) : ds.GetXmlSchema());
                WriteComma();
            }
            bool tablesep = false;
            foreach (DataTable table in ds.Tables)
            {
                if (tablesep) WriteComma();
                tablesep = true;
                WriteDataTableData(table);
            }
            // end dataset
            _output.WriteByte(TOKENS.DOC_END);
        }

        private void WriteDataTableData(DataTable table)
        {
            WriteName(table.TableName);
            WriteColon();
            _output.WriteByte(TOKENS.ARRAY_START);
            DataColumnCollection cols = table.Columns;
            bool rowseparator = false;
            foreach (DataRow row in table.Rows)
            {
                if (rowseparator) WriteComma();
                rowseparator = true;
                _output.WriteByte(TOKENS.ARRAY_START);

                bool pendingSeperator = false;
                foreach (DataColumn column in cols)
                {
                    if (pendingSeperator) WriteComma();
                    WriteValue(row[column]);
                    pendingSeperator = true;
                }
                _output.WriteByte(TOKENS.ARRAY_END);
            }

            _output.WriteByte(TOKENS.ARRAY_END);
        }

        void WriteDataTable(DataTable dt)
        {
            _output.WriteByte(TOKENS.DOC_START);
            //if (this.useExtension)
            {
                this.WritePair("$schema", _params.UseOptimizedDatasetSchema ? (object?)this.GetSchema(dt) : this.GetXmlSchema(dt));
                WriteComma();
            }

            WriteDataTableData(dt);

            // end datatable
            _output.WriteByte(TOKENS.DOC_END);
        }
        bool _TypesWritten = false;

        private void WriteObject(object obj)
        {
            int i = 0;
            if (_cirobj.TryGetValue(obj, out i) == false)
                _cirobj.Add(obj, _cirobj.Count + 1);
            else
            {
                if (_current_depth > 0)
                {
                    //_circular = true;
                    _output.WriteByte(TOKENS.DOC_START);
                    WriteName("$i");
                    WriteColon();
                    WriteValue(i);
                    _output.WriteByte(TOKENS.DOC_END);
                    return;
                }
            }
            if (_params.UsingGlobalTypes == false)
                _output.WriteByte(TOKENS.DOC_START);
            else
            {
                if (_TypesWritten == false)
                {
                    _output.WriteByte(TOKENS.DOC_START);
                    // write pointer to $types position
                    _output.WriteByte(TOKENS.TYPES_POINTER);
                    _typespointer = (int)_output.Length; // place holder
                    WriteInt32Raw(0); // zero pointer for now
                                                      //_output = new MemoryStream();
                    _TypesWritten = true;
                }
                else
                    _output.WriteByte(TOKENS.DOC_START);

            }
            _current_depth++;
            if (_current_depth > _MAX_DEPTH)
                throw new Exception("Serializer encountered maximum depth of " + _MAX_DEPTH);

            Type t = obj.GetType();
            bool append = false;
            if (_params.UseExtensions)
            {
                if (_params.UsingGlobalTypes == false)
                    WritePairFast("$type", Reflection.Instance.GetTypeAssemblyName(t));
                else
                    WritePairFast("$type", GetGlobalTypeId(t));
                append = true;
            }

            Getters[] g = Reflection.Instance.GetGetters(t, /*_params.ShowReadOnlyProperties,*/ _params.IgnoreAttributes);
            int c = g.Length;
            for (int ii = 0; ii < c; ii++)
            {
                var p = g[ii];
                var o = p.Getter(obj);
                if (_params.SerializeNulls == false && (o == null || o is DBNull))
                {

                }
                else
                {
                    if (append)
                        WriteComma();
                    // The DataMember name when there is one - the reader has expected it since upstream
                    // v1.4.23, but no writer ever wrote it until this fork.
                    WritePair(p.memberName ?? p.Name, o);
                    append = true;
                }
            }
            _output.WriteByte(TOKENS.DOC_END);
            _current_depth--;
        }

        /// <summary>
        /// The $types id for <paramref name="t"/>, as the text written into $type.
        /// </summary>
        /// <remarks>
        /// Looked up by Type first. Keyed only by the assembly-qualified name, every object hashed that
        /// whole name (around a hundred characters) and formatted its id into a new string. Ids are
        /// still assigned through <see cref="_globalTypes"/>, so they and the $types table come out
        /// exactly as before - including two types with one name sharing an id.
        /// </remarks>
        private string GetGlobalTypeId(Type t)
        {
            _globalTypeIds ??= new Dictionary<Type, string>();
            if (_globalTypeIds.TryGetValue(t, out string? id))
                return id;

            string ct = Reflection.Instance.GetTypeAssemblyName(t);
            if (_globalTypes.TryGetValue(ct, out int dt) == false)
            {
                dt = _globalTypes.Count + 1;
                _globalTypes.Add(ct, dt);
            }

            id = dt.ToString();
            _globalTypeIds.Add(t, id);
            return id;
        }

        private void WritePairFast(string name, string value)
        {
            if (_params.SerializeNulls == false && (value == null))
                return;
            WriteName(name);

            WriteColon();

            WriteString(value);
        }

        private void WritePair(string name, object? value)
        {
            if (_params.SerializeNulls == false && (value == null || value is DBNull))
                return;
            WriteName(name);

            WriteColon();

            WriteValue(value);
        }

        private void WriteArray(IEnumerable array)
        {
            _output.WriteByte(TOKENS.ARRAY_START);

            bool pendingSeperator = false;

            foreach (object obj in array)
            {
                if (pendingSeperator) WriteComma();

                WriteValue(obj);

                pendingSeperator = true;
            }
            _output.WriteByte(TOKENS.ARRAY_END);
        }

        private void WriteStringDictionary(IDictionary dic)
        {
            _output.WriteByte(TOKENS.DOC_START);

            bool pendingSeparator = false;

            foreach (DictionaryEntry entry in dic)
            {
                if (pendingSeparator) WriteComma();

                WritePair((string)entry.Key, entry.Value);

                pendingSeparator = true;
            }
            _output.WriteByte(TOKENS.DOC_END);
        }

        private void WriteStringDictionary(IDictionary<string, object> dic)
        {
            _output.WriteByte(TOKENS.DOC_START);

            bool pendingSeparator = false;

            foreach (KeyValuePair<string, object> entry in dic)
            {
                if (pendingSeparator) WriteComma();

                WritePair((string)entry.Key, entry.Value);

                pendingSeparator = true;
            }
            _output.WriteByte(TOKENS.DOC_END);
        }

        private void WriteDictionary(IDictionary dic)
        {
            _output.WriteByte(TOKENS.ARRAY_START);

            bool pendingSeparator = false;

            foreach (DictionaryEntry entry in dic)
            {
                if (pendingSeparator) WriteComma();
                _output.WriteByte(TOKENS.DOC_START);
                WritePair("k", entry.Key);
                WriteComma();
                WritePair("v", entry.Value);
                _output.WriteByte(TOKENS.DOC_END);

                pendingSeparator = true;
            }
            _output.WriteByte(TOKENS.ARRAY_END);
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
                _output.WriteByte(unicode ? TOKENS.NAME_UNI : TOKENS.NAME);
                _output.WriteByte((byte)b.Length);
            }
            else
            {
                _output.WriteByte(unicode ? TOKENS.NAME_UNI_LONG : TOKENS.NAME_LONG);
                WriteInt32Raw(b.Length);
            }

            WriteBytesRaw(b);
        }

        private void WriteString(string s)
        {
            bool unicode = _params.UseUnicodeStrings;
            _output.WriteByte(unicode ? TOKENS.UNICODE_STRING : TOKENS.STRING);
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
            _output.Write(Helper.GetBytes(value, false), 0, 2);
#endif
        }

        private void WriteInt32Raw(int value)
        {
#if NET10_0_OR_GREATER
            Span<byte> buffer = stackalloc byte[sizeof(int)];
            MemoryMarshal.Write(buffer, in value);
            _output.Write(buffer);
#else
            _output.Write(Helper.GetBytes(value, false), 0, 4);
#endif
        }

        private void WriteInt64Raw(long value)
        {
#if NET10_0_OR_GREATER
            Span<byte> buffer = stackalloc byte[sizeof(long)];
            MemoryMarshal.Write(buffer, in value);
            _output.Write(buffer);
#else
            _output.Write(Helper.GetBytes(value, false), 0, 8);
#endif
        }

        // Overwrites four bytes written earlier - the $types pointer placeholder.
        private void PatchInt32(int position, int value)
        {
#if NET10_0_OR_GREATER
            _output.WriteInt32At(position, value);
#else
            _output.Seek(position, SeekOrigin.Begin);
            WriteInt32Raw(value);
#endif
        }

#if NET10_0_OR_GREATER
        /// <summary>
        /// A string whose encoded length is known but whose bytes are not produced yet.
        /// </summary>
        /// <remarks>
        /// Every caller writes a length header before the bytes, so the length has to come first.
        /// Carrying it separately lets <see cref="WriteBytesRaw"/> encode straight into the output
        /// afterwards, instead of encoding into a scratch buffer and copying.
        /// </remarks>
        private readonly record struct PendingString(string Value, bool Unicode, int Length);

        // Only measures; WriteBytesRaw produces the bytes.
        private static PendingString Encode(string s, bool unicode) =>
            new PendingString(s, unicode, unicode ? s.Length * sizeof(char) : Reflection.UTF8GetByteCount(s));

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
            int written = Reflection.UTF8GetBytes(pending.Value, _output.GetSpan(pending.Length));
            if (written != pending.Length)
                throw new InvalidOperationException($"UTF-8 encoder wrote {written} bytes after counting {pending.Length}.");

            _output.Advance(written);
        }

        private void WriteBytesRaw(ReadOnlySpan<byte> bytes) => _output.Write(bytes);

        // Idempotent: called from ConvertToBJSON's finally and again from Dispose.
        private void ReleasePooled() => _output.Dispose();
#else
        private static byte[] Encode(string s, bool unicode) => unicode ? Reflection.UnicodeGetBytes(s) : Reflection.UTF8GetBytes(s);

        private void WriteBytesRaw(byte[] bytes) => _output.Write(bytes, 0, bytes.Length);

        // Nothing is pooled on this target.
        private void ReleasePooled() { }
#endif

        #endregion
    }
}
