using System;
using System.Collections.Generic;
using DuraIT.FastBinaryJson.Internal;
#if NET10_0_OR_GREATER
using System.Runtime.InteropServices;
#endif

namespace DuraIT.FastBinaryJson
{
    internal sealed class BJsonParser
    {
        readonly byte[] _json;
        int _index;
        readonly bool _useUTC = true;
        readonly bool _v1_4TA = false;

        internal BJsonParser(byte[] json, bool useUTC, bool v1_4TA)
        {
            this._json = json;
            _v1_4TA = v1_4TA;
            _useUTC = useUTC;
        }

        public object? Decode()
        {
            bool b = false;
            return ParseValue(out b);
        }

        private Dictionary<string, object> ParseObject()
        {
            Dictionary<string, object> dic = new Dictionary<string, object>(10);
            bool breakparse = false;
            while (!breakparse)
            {
                byte t = GetToken();
                if (t == TOKENS.COMMA)
                    continue;
                if (t == TOKENS.DOC_END)
                    break;
                if (t == TOKENS.TYPES_POINTER)
                {
                    // save curr index position
                    int savedindex = _index;
                    // set index = pointer
                    _index = ParseInt();
                    t = GetToken();
                    // read $types
                    breakparse = readkeyvalue(dic, ref t);
                    // set index = saved + 4
                    _index = savedindex + 4;
                }
                else
                    breakparse = readkeyvalue(dic, ref t);
            }
            return dic;
        }

        private bool readkeyvalue(Dictionary<string, object> dic, ref byte t)
        {
            bool breakparse;
            string key = ReadName(t);

            ReadColon();
            object? val = ParseValue(out breakparse);

            if (!breakparse)
                dic.Add(key, val!);

            return breakparse;
        }

        #region Positional reads for TypedReader

        /*
         * TypedReader walks the same bytes without building the dictionary graph, and uses these to
         * read everything it does not construct itself - so a name, a scalar or a materialised
         * subtree is produced by exactly the code the two-step path runs.
         */

        internal int Index
        {
            get => _index;
            set => _index = value;
        }

        internal byte PeekToken() => _json[_index];

        internal byte ReadToken() => GetToken();

        internal string ReadName(byte token)
        {
            if (token == TOKENS.NAME)
                return ParseName();
            if (token == TOKENS.NAME_UNI)
                return ParseName2();
            if (token == TOKENS.NAME_LONG)
                return ParseLongName(false);
            if (token == TOKENS.NAME_UNI_LONG)
                return ParseLongName(true);

            throw new Exception("excpecting a name field");
        }

        internal void ReadColon()
        {
            if (GetToken() != TOKENS.COLON)
                throw new Exception("expecting a colon");
        }

        /// <summary>
        /// Reads the key whose token is at <paramref name="keyStart"/> - already read - when its bytes
        /// are exactly <paramref name="raw"/>. Returns false, having read nothing more, otherwise.
        /// </summary>
        internal bool TryReadKey(int keyStart, byte[] raw)
        {
            if (keyStart > _json.Length - raw.Length)
                return false;
#if NET10_0_OR_GREATER
            if (!new ReadOnlySpan<byte>(_json, keyStart, raw.Length).SequenceEqual(raw))
                return false;
#else
            for (int i = 0; i < raw.Length; i++)
            {
                if (_json[keyStart + i] != raw[i])
                    return false;
            }
#endif
            _index = keyStart + raw.Length;
            return true;
        }

        /// <summary>
        /// Skips the value at the current index when its bytes are exactly the <paramref name="length"/>
        /// bytes at <paramref name="earlierStart"/>. Returns false, having read nothing, otherwise.
        /// </summary>
        internal bool TrySkipRepeat(int earlierStart, int length)
        {
            if (_index > _json.Length - length)
                return false;
#if NET10_0_OR_GREATER
            if (!new ReadOnlySpan<byte>(_json, _index, length).SequenceEqual(new ReadOnlySpan<byte>(_json, earlierStart, length)))
                return false;
#else
            for (int i = 0; i < length; i++)
            {
                if (_json[_index + i] != _json[earlierStart + i])
                    return false;
            }
#endif
            _index += length;
            return true;
        }

        /// <summary>
        /// A copy of the bytes from <paramref name="start"/> up to the current index.
        /// </summary>
        internal byte[] CopyFrom(int start)
        {
            byte[] bytes = new byte[_index - start];
            Buffer.BlockCopy(_json, start, bytes, 0, bytes.Length);
            return bytes;
        }

        /// <summary>
        /// Reads one value, materialising it as the two-step path does when it is an object or array.
        /// </summary>
        internal object? ReadValue(out bool breakparse) => ParseValue(out breakparse);

#if NET10_0_OR_GREATER
        /// <summary>
        /// Reads a STRING or UNICODE_STRING value as chars without allocating it: UTF-16 in place,
        /// UTF-8 decoded into <paramref name="buffer"/>. Returns false, having read nothing, for any
        /// other token or a UTF-8 value that might not fit the buffer.
        /// </summary>
        /// <remarks>
        /// The same bytes ParseUnicodeString and ParseString turn into a string, decoded the same way:
        /// UTF-16 drops an odd trailing byte exactly as UnicodeGetString does, and UTF-8 goes through
        /// the same encoder instance.
        /// </remarks>
        /// <exception cref="ArgumentOutOfRangeException">If the length runs past the end of the payload.</exception>
        internal bool TryReadStringChars(Span<char> buffer, out ReadOnlySpan<char> chars)
        {
            chars = default;
            byte token = _json[_index];
            if (token != TOKENS.STRING && token != TOKENS.UNICODE_STRING)
                return false;

            int length = Helper.ToInt32(_json, _index + 1);
            int start = _index + 5;
            if (length < 0 || start > _json.Length - length)
                throw new ArgumentOutOfRangeException(nameof(length), "String length runs past the end of the payload.");

            ReadOnlySpan<byte> bytes = new ReadOnlySpan<byte>(_json, start, length);
            if (token == TOKENS.UNICODE_STRING)
            {
                chars = MemoryMarshal.Cast<byte, char>(bytes);
            }
            else
            {
                // A UTF-8 byte never yields more than one char, so this bounds the decoded length.
                if (length > buffer.Length)
                    return false;

                chars = buffer.Slice(0, Reflection.UTF8GetChars(bytes, buffer));
            }

            _index = start + length;
            return true;
        }
#endif

        /*
         * The Parse* readers for the primitive tokens are internal for TypedReader, which calls them
         * after reading the token itself to set a member without boxing. ParseValue boxes the result
         * of the very same methods, which is what makes the two paths agree by construction.
         */

        /// <summary>
        /// Follows a TYPES_POINTER (its token already read) and returns the $types table, leaving the
        /// index just past the pointer - the same jump ParseObject makes.
        /// </summary>
        internal Dictionary<string, object> ReadTypesTable()
        {
            Dictionary<string, object> dic = new Dictionary<string, object>();
            int savedindex = _index;
            _index = ParseInt();
            byte t = GetToken();
            readkeyvalue(dic, ref t);
            _index = savedindex + 4;
            return dic;
        }

        #endregion

        private string ParseName2() // unicode byte len string -> <128 len chars
        {
            byte c = _json[_index++];
#if NET10_0_OR_GREATER
            string s = NameCache.FromUtf16(_json, _index, c);
#else
            string s = Reflection.UnicodeGetString(_json, _index, c);
#endif
            _index += c;
            return s;
        }

        private string ParseName()
        {
            byte c = _json[_index++];
#if NET10_0_OR_GREATER
            string s = NameCache.FromUtf8(_json, _index, c);
#else
            string s = Reflection.UTF8GetString(_json, _index, c);
#endif
            _index += c;
            return s;
        }

        /// <summary>
        /// Reads a name written in the four-byte length form, for names of 256 encoded bytes or more.
        /// </summary>
        /// <remarks>
        /// Distinct from ParseNameLong, which serves the typed-array type name and reads a TWO-byte
        /// length under its own token. Four bytes here to match WriteString and to leave no second
        /// truncation threshold behind - a two-byte length would just move the cliff to 64k.
        /// </remarks>
        private string ParseLongName(bool unicode)
        {
            int c = Helper.ToInt32(_json, _index);
            _index += 4;
            string s = unicode ? Reflection.UnicodeGetString(_json, _index, c) : Reflection.UTF8GetString(_json, _index, c);
            _index += c;
            return s;
        }

        private List<object> ParseArray()
        {
            List<object> array = new List<object>();

            bool breakparse = false;
            while (!breakparse)
            {
                object? o = ParseValue(out breakparse);
                byte t = 0;
                if (!breakparse)
                {
                    array.Add(o!);
                    t = GetToken();
                }
                else
                    t = (byte)o!;

                if (t == TOKENS.COMMA)
                    continue;
                if (t == TOKENS.ARRAY_END)
                    break;
            }
            return array;
        }

        private object? ParseValue(out bool breakparse)
        {
            byte t = GetToken();
            breakparse = false;
            switch (t)
            {
                case TOKENS.BYTE:
                    return ParseByte();
                case TOKENS.SBYTE:
                    return ParseSByte();
                case TOKENS.DATETIMEOFFSET:
                    return ParseDateTimeOffset();
                case TOKENS.BYTEARRAY:
                    return ParseByteArray();
                case TOKENS.CHAR:
                    return ParseChar();
                case TOKENS.DATETIME:
                    return ParseDateTime();
                case TOKENS.DECIMAL:
                    return ParseDecimal();
                case TOKENS.DOUBLE:
                    return ParseDouble();
                case TOKENS.FLOAT:
                    return ParseFloat();
                case TOKENS.GUID:
                    return ParseGuid();
                case TOKENS.INT:
                    return ParseInt();
                case TOKENS.LONG:
                    return ParseLong();
                case TOKENS.SHORT:
                    return ParseShort();
                case TOKENS.UINT:
                    return ParseUint();
                case TOKENS.ULONG:
                    return ParseULong();
                case TOKENS.USHORT:
                    return ParseUShort();
                case TOKENS.UNICODE_STRING:
                    return ParseUnicodeString();
                case TOKENS.STRING:
                    return ParseString();
                case TOKENS.DOC_START:
                    return ParseObject();
                case TOKENS.ARRAY_START:
                    return ParseArray();
                case TOKENS.TRUE:
                    return true;
                case TOKENS.FALSE:
                    return false;
                case TOKENS.NULL:
                    return null;
                case TOKENS.ARRAY_END:
                    breakparse = true;
                    return TOKENS.ARRAY_END;
                case TOKENS.DOC_END:
                    breakparse = true;
                    return TOKENS.DOC_END;
                case TOKENS.COMMA:
                    breakparse = true;
                    return TOKENS.COMMA;
                case TOKENS.ARRAY_TYPED:
                case TOKENS.ARRAY_TYPED_LONG:
                    return ParseTypedArray(t);
                case TOKENS.TIMESPAN:
                    return ParsTimeSpan();
            }

            throw new Exception("Unrecognized token at index = " + _index);
        }

        internal TimeSpan ParsTimeSpan()
        {
            long l = Helper.ToInt64(_json, _index);
            _index += 8;

            TimeSpan dt = new TimeSpan(l);

            return dt;
        }

        private object ParseTypedArray(byte token)
        {
            TypedArray ar = new TypedArray();
            if (token == TOKENS.ARRAY_TYPED)
            {
                if (_v1_4TA)
                    ar.typename = ParseName();
                else
                    ar.typename = ParseName2();
            }
            else
                ar.typename = ParseNameLong();

            ar.count = ParseInt();

            bool breakparse = false;
            while (!breakparse)
            {
                object? o = ParseValue(out breakparse);
                byte b = 0;
                if (!breakparse)
                {
                    ar.data.Add(o!);
                    b = GetToken();
                }
                else
                    b = (byte)o!;

                if (b == TOKENS.COMMA)
                    continue;
                if (b == TOKENS.ARRAY_END)
                    break;
            }
            return ar;
        }

        private string ParseNameLong() // unicode short len string -> <32k chars
        {
            short c = Helper.ToInt16(_json, _index);
            _index += 2;
            string s = Reflection.UnicodeGetString(_json, _index, c);
            _index += c;
            return s;
        }

        /*
         * WriteChar writes the char as an Int16, and this returned that Int16 uncast, so no char
         * ever survived a round trip: a typed property threw InvalidCastException and an untyped
         * read silently yielded a boxed Int16. The cast back is the whole fix - the written bytes
         * were always correct, so nothing about the wire format moves here.
         */
        internal char ParseChar()
        {
            short u = Helper.ToInt16(_json, _index);
            _index += 2;
            return unchecked((char)u);
        }

        internal Guid ParseGuid()
        {
#if NET10_0_OR_GREATER
            Guid g = new Guid(new ReadOnlySpan<byte>(_json, _index, 16));
            _index += 16;
            return g;
#else
            byte[] b = new byte[16];
            Buffer.BlockCopy(_json, _index, b, 0, 16);
            _index += 16;
            return new Guid(b);
#endif
        }

        internal float ParseFloat()
        {
            float f = BitConverter.ToSingle(_json, _index);
            _index += 4;
            return f;
        }

        internal ushort ParseUShort()
        {
            ushort u = (ushort)Helper.ToInt16(_json, _index);
            _index += 2;
            return u;
        }

        internal ulong ParseULong()
        {
            ulong u = (ulong)Helper.ToInt64(_json, _index);
            _index += 8;
            return u;
        }

        internal uint ParseUint()
        {
            uint u = (uint)Helper.ToInt32(_json, _index);
            _index += 4;
            return u;
        }

        internal short ParseShort()
        {
            short u = Helper.ToInt16(_json, _index);
            _index += 2;
            return u;
        }

        internal long ParseLong()
        {
            long u = Helper.ToInt64(_json, _index);
            _index += 8;
            return u;
        }

        internal int ParseInt()
        {
            int u = Helper.ToInt32(_json, _index);
            _index += 4;
            return u;
        }

        internal double ParseDouble()
        {
            double d = BitConverter.ToDouble(_json, _index);
            _index += 8;
            return d;
        }

        private object ParseUnicodeString()
        {
            int c = Helper.ToInt32(_json, _index);
            _index += 4;

            string s = Reflection.UnicodeGetString(_json, _index, c);
            _index += c;
            return s;
        }

        private string ParseString()
        {
            int c = Helper.ToInt32(_json, _index);
            _index += 4;

            string s = Reflection.UTF8GetString(_json, _index, c);
            _index += c;
            return s;
        }

        internal decimal ParseDecimal()
        {
#if NET10_0_OR_GREATER
            Span<int> i = stackalloc int[4];
#else
            int[] i = new int[4];
#endif
            i[0] = Helper.ToInt32(_json, _index);
            _index += 4;
            i[1] = Helper.ToInt32(_json, _index);
            _index += 4;
            i[2] = Helper.ToInt32(_json, _index);
            _index += 4;
            i[3] = Helper.ToInt32(_json, _index);
            _index += 4;

            return new decimal(i);
        }

        /*
         * This used to call ToLocalTime while the write path called ToUniversalTime, so a value
         * written as Utc came back as Local, shifted by the READING machine's offset. The bytes were
         * always correct and machine independent; the asymmetry made the restored value depend on
         * where it was read. Under TZ=UTC the two cancelled out, which is how it went unnoticed.
         *
         * Labelling the ticks instead of converting them is what makes the round trip symmetric. No
         * byte changes - this is a read-side fix - but a caller using UseUTCDateTime now receives
         * the instant that was written rather than a local rendering of it.
         */
        internal DateTime ParseDateTime()
        {
            long l = Helper.ToInt64(_json, _index);
            _index += 8;

            return _useUTC ? new DateTime(l, DateTimeKind.Utc) : new DateTime(l);
        }

        internal DateTimeOffset ParseDateTimeOffset()
        {
            long ticks = Helper.ToInt64(_json, _index);
            _index += 8;
            short offsetMinutes = Helper.ToInt16(_json, _index);
            _index += 2;

            return new DateTimeOffset(ticks, TimeSpan.FromMinutes(offsetMinutes));
        }

        private byte[] ParseByteArray()
        {
            int c = Helper.ToInt32(_json, _index);
            _index += 4;
            // Checked before allocating: a crafted length would otherwise allocate up to 2 GB first.
            if (c < 0 || c > _json.Length - _index)
                throw new ArgumentOutOfRangeException(nameof(c), "Byte array length runs past the end of the payload.");
            byte[] b = new byte[c];
            Buffer.BlockCopy(_json, _index, b, 0, c);
            _index += c;
            return b;
        }

        internal byte ParseByte()
        {
            return _json[_index++];
        }

        private sbyte ParseSByte()
        {
            return unchecked((sbyte)_json[_index++]);
        }

        private byte GetToken()
        {
            byte b = _json[_index++];
            return b;
        }
    }
}
