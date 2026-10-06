using System;
using System.Collections.Generic;
using DuraIT.FastBinaryJson.Internal;
#if NET10_0_OR_GREATER
using System.Runtime.InteropServices;
#endif

namespace DuraIT.FastBinaryJson
{
    internal sealed class BjsonParser
    {
        readonly byte[] _json;
        readonly bool _useUTC;
        readonly bool _v1_4TA;

        internal BjsonParser(byte[] json, bool useUTC, bool v1_4TA)
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
                if (t == Tokens.Comma)
                    continue;
                if (t == Tokens.DocEnd)
                    break;
                if (t == Tokens.TypesPointer)
                {
                    // save curr index position
                    int savedindex = Index;
                    // set index = pointer
                    Index = ParseInt();
                    t = GetToken();
                    // read $types
                    breakparse = readkeyvalue(dic, ref t);
                    // set index = saved + 4
                    Index = savedindex + 4;
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

        internal int Index { get; set; }

        internal byte PeekToken() => _json[Index];

        internal byte ReadToken() => GetToken();

        internal string ReadName(byte token)
        {
            if (token == Tokens.Name)
                return ParseName();
            if (token == Tokens.NameUtf16)
                return ParseName2();
            if (token == Tokens.NameLong)
                return ParseLongName(false);
            if (token == Tokens.NameUtf16Long)
                return ParseLongName(true);

            throw new BjsonException("excpecting a name field");
        }

        internal void ReadColon()
        {
            if (GetToken() != Tokens.Colon)
                throw new BjsonException("expecting a colon");
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
            Index = keyStart + raw.Length;
            return true;
        }

        /// <summary>
        /// Skips the value at the current index when its bytes are exactly the <paramref name="length"/>
        /// bytes at <paramref name="earlierStart"/>. Returns false, having read nothing, otherwise.
        /// </summary>
        internal bool TrySkipRepeat(int earlierStart, int length)
        {
            if (Index > _json.Length - length)
                return false;
#if NET10_0_OR_GREATER
            if (!new ReadOnlySpan<byte>(_json, Index, length).SequenceEqual(new ReadOnlySpan<byte>(_json, earlierStart, length)))
                return false;
#else
            for (int i = 0; i < length; i++)
            {
                if (_json[Index + i] != _json[earlierStart + i])
                    return false;
            }
#endif
            Index += length;
            return true;
        }

        /// <summary>
        /// A copy of the bytes from <paramref name="start"/> up to the current index.
        /// </summary>
        internal byte[] CopyFrom(int start)
        {
            byte[] bytes = new byte[Index - start];
            Buffer.BlockCopy(_json, start, bytes, 0, bytes.Length);
            return bytes;
        }

        /// <summary>
        /// Reads one value, materialising it as the two-step path does when it is an object or array.
        /// </summary>
        internal object? ReadValue(out bool breakparse) => ParseValue(out breakparse);

#if NET10_0_OR_GREATER
        /// <summary>
        /// Reads a Tokens.Utf8String or Tokens.Utf16String value as chars without allocating it: UTF-16 in place,
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
            byte token = _json[Index];
            if (token != Tokens.Utf8String && token != Tokens.Utf16String)
                return false;

            int length = Helper.ToInt32(_json, Index + 1);
            int start = Index + 5;
            Helper.CheckLength(_json, start, length, "String");

            ReadOnlySpan<byte> bytes = new ReadOnlySpan<byte>(_json, start, length);
            if (token == Tokens.Utf16String)
            {
                chars = MemoryMarshal.Cast<byte, char>(bytes);
            }
            else
            {
                // A UTF-8 byte never yields more than one char, so this bounds the decoded length.
                if (length > buffer.Length)
                    return false;

                chars = buffer.Slice(0, TypeReflector.UTF8GetChars(bytes, buffer));
            }

            Index = start + length;
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
            int savedindex = Index;
            Index = ParseInt();
            byte t = GetToken();
            readkeyvalue(dic, ref t);
            Index = savedindex + 4;
            return dic;
        }

        #endregion

        private string ParseName2() // unicode byte len string -> <128 len chars
        {
            byte c = _json[Index++];
#if NET10_0_OR_GREATER
            string s = NameCache.FromUtf16(_json, Index, c);
#else
            string s = TypeReflector.UnicodeGetString(_json, Index, c);
#endif
            Index += c;
            return s;
        }

        private string ParseName()
        {
            byte c = _json[Index++];
#if NET10_0_OR_GREATER
            string s = NameCache.FromUtf8(_json, Index, c);
#else
            string s = TypeReflector.UTF8GetString(_json, Index, c);
#endif
            Index += c;
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
            int c = Helper.ToInt32(_json, Index);
            Index += 4;
            string s = unicode ? TypeReflector.UnicodeGetString(_json, Index, c) : TypeReflector.UTF8GetString(_json, Index, c);
            Index += c;
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

                if (t == Tokens.Comma)
                    continue;
                if (t == Tokens.ArrayEnd)
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
                case Tokens.Byte:
                    return ParseByte();
                case Tokens.SByte:
                    return ParseSByte();
                case Tokens.DateTimeOffset:
                    return ParseDateTimeOffset();
                case Tokens.ByteArray:
                    return ParseByteArray();
                case Tokens.Char:
                    return ParseChar();
                case Tokens.DateTime:
                    return ParseDateTime();
                case Tokens.Decimal:
                    return ParseDecimal();
                case Tokens.Double:
                    return ParseDouble();
                case Tokens.Single:
                    return ParseFloat();
                case Tokens.Guid:
                    return ParseGuid();
                case Tokens.Int32:
                    return ParseInt();
                case Tokens.Int64:
                    return ParseLong();
                case Tokens.Int16:
                    return ParseShort();
                case Tokens.UInt32:
                    return ParseUint();
                case Tokens.UInt64:
                    return ParseULong();
                case Tokens.UInt16:
                    return ParseUShort();
                case Tokens.Utf16String:
                    return ParseUnicodeString();
                case Tokens.Utf8String:
                    return ParseString();
                case Tokens.DocStart:
                    return ParseObject();
                case Tokens.ArrayStart:
                    return ParseArray();
                case Tokens.True:
                    return true;
                case Tokens.False:
                    return false;
                case Tokens.Null:
                    return null;
                case Tokens.ArrayEnd:
                    breakparse = true;
                    return Tokens.ArrayEnd;
                case Tokens.DocEnd:
                    breakparse = true;
                    return Tokens.DocEnd;
                case Tokens.Comma:
                    breakparse = true;
                    return Tokens.Comma;
                case Tokens.TypedArray:
                case Tokens.TypedArrayLong:
                    return ParseTypedArray(t);
                case Tokens.TimeSpan:
                    return ParsTimeSpan();
            }

            throw new BjsonException("Unrecognized token at index = " + Index);
        }

        internal TimeSpan ParsTimeSpan()
        {
            long l = Helper.ToInt64(_json, Index);
            Index += 8;

            TimeSpan dt = new TimeSpan(l);

            return dt;
        }

        private TypedArray ParseTypedArray(byte token)
        {
            TypedArray ar = new TypedArray();
            if (token == Tokens.TypedArray)
            {
                if (_v1_4TA)
                    ar.TypeName = ParseName();
                else
                    ar.TypeName = ParseName2();
            }
            else
                ar.TypeName = ParseNameLong();

            ar.Count = ParseInt();

            bool breakparse = false;
            while (!breakparse)
            {
                object? o = ParseValue(out breakparse);
                byte b = 0;
                if (!breakparse)
                {
                    ar.DataList.Add(o!);
                    b = GetToken();
                }
                else
                    b = (byte)o!;

                if (b == Tokens.Comma)
                    continue;
                if (b == Tokens.ArrayEnd)
                    break;
            }
            return ar;
        }

        private string ParseNameLong() // unicode short len string -> <32k chars
        {
            short c = Helper.ToInt16(_json, Index);
            Index += 2;
            string s = TypeReflector.UnicodeGetString(_json, Index, c);
            Index += c;
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
            short u = Helper.ToInt16(_json, Index);
            Index += 2;
            return unchecked((char)u);
        }

        internal Guid ParseGuid()
        {
#if NET10_0_OR_GREATER
            Guid g = new Guid(new ReadOnlySpan<byte>(_json, Index, 16));
            Index += 16;
            return g;
#else
            byte[] b = new byte[16];
            Buffer.BlockCopy(_json, Index, b, 0, 16);
            Index += 16;
            return new Guid(b);
#endif
        }

        internal float ParseFloat()
        {
            float f = BitConverter.ToSingle(_json, Index);
            Index += 4;
            return f;
        }

        internal ushort ParseUShort()
        {
            ushort u = (ushort)Helper.ToInt16(_json, Index);
            Index += 2;
            return u;
        }

        internal ulong ParseULong()
        {
            ulong u = (ulong)Helper.ToInt64(_json, Index);
            Index += 8;
            return u;
        }

        internal uint ParseUint()
        {
            uint u = (uint)Helper.ToInt32(_json, Index);
            Index += 4;
            return u;
        }

        internal short ParseShort()
        {
            short u = Helper.ToInt16(_json, Index);
            Index += 2;
            return u;
        }

        internal long ParseLong()
        {
            long u = Helper.ToInt64(_json, Index);
            Index += 8;
            return u;
        }

        internal int ParseInt()
        {
            int u = Helper.ToInt32(_json, Index);
            Index += 4;
            return u;
        }

        internal double ParseDouble()
        {
            double d = BitConverter.ToDouble(_json, Index);
            Index += 8;
            return d;
        }

        private string ParseUnicodeString()
        {
            int c = Helper.ToInt32(_json, Index);
            Index += 4;

            string s = TypeReflector.UnicodeGetString(_json, Index, c);
            Index += c;
            return s;
        }

        private string ParseString()
        {
            int c = Helper.ToInt32(_json, Index);
            Index += 4;

            string s = TypeReflector.UTF8GetString(_json, Index, c);
            Index += c;
            return s;
        }

        internal decimal ParseDecimal()
        {
#if NET10_0_OR_GREATER
            Span<int> i = stackalloc int[4];
#else
            int[] i = new int[4];
#endif
            i[0] = Helper.ToInt32(_json, Index);
            Index += 4;
            i[1] = Helper.ToInt32(_json, Index);
            Index += 4;
            i[2] = Helper.ToInt32(_json, Index);
            Index += 4;
            i[3] = Helper.ToInt32(_json, Index);
            Index += 4;

            return new decimal(i);
        }

        /*
         * This used to call ToLocalTime while the write path called ToUniversalTime, so a value
         * written as Utc came back as Local, shifted by the READING machine's offset. The bytes were
         * always correct and machine independent; the asymmetry made the restored value depend on
         * where it was read. Under TZ=UTC the two cancelled out, which is how it went unnoticed.
         *
         * Labelling the ticks instead of converting them is what makes the round trip symmetric. No
         * byte changes - this is a read-side fix - but a caller using UseUtcDateTime now receives
         * the instant that was written rather than a local rendering of it.
         */
        internal DateTime ParseDateTime()
        {
            long l = Helper.ToInt64(_json, Index);
            Index += 8;

            return _useUTC ? new DateTime(l, DateTimeKind.Utc) : new DateTime(l, DateTimeKind.Unspecified);
        }

        internal DateTimeOffset ParseDateTimeOffset()
        {
            long ticks = Helper.ToInt64(_json, Index);
            Index += 8;
            short offsetMinutes = Helper.ToInt16(_json, Index);
            Index += 2;

            return new DateTimeOffset(ticks, TimeSpan.FromMinutes(offsetMinutes));
        }

        private byte[] ParseByteArray()
        {
            int c = Helper.ToInt32(_json, Index);
            Index += 4;
            // Checked before allocating: a crafted length would otherwise allocate up to 2 GB first.
            Helper.CheckLength(_json, Index, c, "Byte array");
            byte[] b = new byte[c];
            Buffer.BlockCopy(_json, Index, b, 0, c);
            Index += c;
            return b;
        }

        internal byte ParseByte()
        {
            return _json[Index++];
        }

        private sbyte ParseSByte()
        {
            return unchecked((sbyte)_json[Index++]);
        }

        private byte GetToken()
        {
            byte b = _json[Index++];
            return b;
        }
    }
}
