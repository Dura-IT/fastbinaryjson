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
        // "$type" as each encoding writes it, so the head key of an object is recognised without decoding.
        private static readonly byte[] TypeKeyUtf16 = Key(Tokens.NameUtf16, TypeReflector.UnicodeGetBytes(WireKeys.Type));
        private static readonly byte[] TypeKeyUtf8 = Key(Tokens.Name, TypeReflector.Utf8GetBytes(WireKeys.Type));

        private readonly Deserializer _deserializer;
        private readonly BjsonParser _parser;

        // The last $type value resolved with no $types table in play: where its bytes are, and its type.
        private int _lastTypeStart;
        private int _lastTypeLength;
        private Type? _lastType;

        private TypedReader(Deserializer deserializer, BjsonParser parser)
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

            BjsonParameters parameters = deserializer.Parameters;
            TypedReader reader = new TypedReader(deserializer, new BjsonParser(json, parameters.UseUtcDateTime, parameters.UseV14TypedArray));

            // The same shapes Deserializer.ToObject sends to ParseDictionary and RootList.
            if (json[0] == Tokens.DocStart && IsPlainRootObject(type, genericDefinition))
                return reader.TryReadObject(type, null, false, out result);

            if (json[0] == Tokens.ArrayStart && genericDefinition == typeof(List<>))
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

            return type == typeof(object) || !typeof(IEnumerable).IsAssignableFrom(type);
        }

        /// <summary>
        /// A member ConvertValue would build with ParseDictionary when handed a dictionary.
        /// </summary>
        private static bool IsPlainObjectMember(PropertyMetadata pi)
        {
            if (IsSpecialMember(pi))
                return false;
            if (pi.IsGenericType && !pi.IsValueType)
                return false;

            return pi.IsClass || pi.IsStruct || pi.IsInterface;
        }

        /// <summary>
        /// A member ConvertValue would build with CreateGenericList when handed a list.
        /// </summary>
        private static bool IsPlainListMember(PropertyMetadata pi)
        {
            return !IsSpecialMember(pi) && pi.IsGenericType && !pi.IsValueType;
        }

        // The members ConvertValue's switch handles itself - always left to it.
        private static bool IsSpecialMember(PropertyMetadata pi)
        {
            switch (pi.Type)
            {
                case PropertyKind.DataSet:
                case PropertyKind.DataTable:
                case PropertyKind.Custom:
                case PropertyKind.Enum:
                case PropertyKind.SByte:
                case PropertyKind.StringKeyDictionary:
                case PropertyKind.Hashtable:
                case PropertyKind.Dictionary:
                case PropertyKind.NameValue:
                case PropertyKind.StringDictionary:
                case PropertyKind.Array:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// The equivalent of ParseDictionary(dictionary, globaltypes, declared, null) for the object at
        /// the current index. Returns false with the index back on its Tokens.DocStart, and every side effect
        /// undone, when the object has to go through the two-step path.
        /// </summary>
        /// <param name="declared">The declared type of the member or element, or null when the value's own $type decides.</param>
        /// <param name="globaltypes">The $types table in effect, if any.</param>
        /// <param name="mayExtendSharedTypes">
        /// True only for an element of a root list, whose $types table lands in the list's shared one.
        /// </param>
        /// <param name="result">The object that was read, when this returns true.</param>
        /// <returns>True when the object was read here; false when it has to go through the two-step path.</returns>
        private bool TryReadObject(Type? declared, Dictionary<string, object>? globaltypes, bool mayExtendSharedTypes, out object? result)
        {
            result = null;
            if (declared == typeof(NameValueCollection) || declared == typeof(StringDictionary))
            {
                _deserializer.OneStepFallbacks++;
                return false;
            }

            ReadAttempt attempt = new ReadAttempt(_parser.Index, _deserializer.CircularCount, globaltypes, declared);

            _parser.ReadToken(); // Tokens.DocStart
            byte t = ReadSkippingCommas();

            if (t == Tokens.TypesPointer)
            {
                if (!TryReadTypesTable(ref attempt, mayExtendSharedTypes))
                    return Abandon(attempt);

                t = ReadSkippingCommas();
            }

            HeadOutcome head = ReadHead(ref attempt, t, out object? reference);
            if (head == HeadOutcome.Abandoned)
                return Abandon(attempt);

            if (head == HeadOutcome.Reference)
            {
                result = reference;
                return true;
            }

            // No $type: ParseDictionary returns the dictionary itself for object, and fails for the rest.
            Type? type = attempt.Type;
            if (type == null || (attempt.NeedsDeclaredType && (type == typeof(object) || type.IsAbstract || type.IsInterface)))
                return Abandon(attempt);

            object o = _deserializer.CreateInstance(type);
            int number = _deserializer.RegisterCircular(o);
            WireNameMap members = TypeReflector.Instance.GetWireNameMap(type, type.FullName!, _deserializer.Parameters.ShowReadOnlyProperties);

            if (t != Tokens.DocEnd && !TryReadMembers(attempt, members, ref o))
                return Abandon(attempt);

            if (t != Tokens.DocEnd && type.IsValueType)
                _deserializer.UpdateCircular(number, o);
            result = o;
            return true;
        }

        /// <summary>
        /// Merges a leading $types table into the table in effect. False when the object must be abandoned.
        /// </summary>
        private bool TryReadTypesTable(ref ReadAttempt attempt, bool mayExtendSharedTypes)
        {
            if (attempt.Globaltypes != null && (!mayExtendSharedTypes || attempt.Globaltypes.Count > 0))
                return false;

            Dictionary<string, object> table = _parser.ReadTypesTable();
            if (table.Count != 1 || !table.TryGetValue("$types", out object? types))
                return false;

            if (attempt.Globaltypes == null)
                attempt.Globaltypes = new Dictionary<string, object>();
            else
                attempt.AddedTypes = new List<string>();

            foreach (KeyValuePair<string, object> kv in (Dictionary<string, object>)types)
            {
                attempt.Globaltypes.Add(kv.Key, kv.Value);
                attempt.AddedTypes?.Add(kv.Key);
            }

            attempt.ReadTypes = true;
            return true;
        }

        /// <summary>
        /// Reads the first key of the object: a $i reference, the $type, or the first ordinary member.
        /// </summary>
        private HeadOutcome ReadHead(ref ReadAttempt attempt, byte t, out object? reference)
        {
            reference = null;
            if (t == Tokens.DocEnd)
                return HeadOutcome.Continue;

            string name = ReadHeadName(t);
            _parser.ReadColon();

            if (name == "$i")
                return ReadReferenceHead(attempt, out reference);

            if (name == WireKeys.Type)
                return TryReadTypeHead(ref attempt) ? HeadOutcome.Continue : HeadOutcome.Abandoned;

            if (IsSpecialName(name))
                return HeadOutcome.Abandoned;

            attempt.FirstMember = name;
            return HeadOutcome.Continue;
        }

        private HeadOutcome ReadReferenceHead(ReadAttempt attempt, out object? reference)
        {
            reference = null;

            // ParseDictionary resolves $i before it merges $types, so it never merges both.
            if (attempt.ReadTypes)
                return HeadOutcome.Abandoned;

            object? id = _parser.ReadValue(out bool broke);
            if (broke || _parser.PeekToken() != Tokens.DocEnd)
                return HeadOutcome.Abandoned;

            _parser.ReadToken();
            reference = _deserializer.ResolveCircular(id!);
            return HeadOutcome.Reference;
        }

        private bool TryReadTypeHead(ref ReadAttempt attempt)
        {
            if (!TryRepeatType(attempt.Globaltypes, out Type? type))
            {
                int valueStart = _parser.Index;
                if (!TryResolveTypeInPlace(attempt.Globaltypes, out type))
                {
                    object? tn = _parser.ReadValue(out bool broke);
                    if (broke)
                        return false;

                    type = _deserializer.ResolveType(tn!, attempt.Globaltypes);
                }

                RememberType(attempt.Globaltypes, valueStart, type);
            }

            attempt.Type = type;
            attempt.NeedsDeclaredType = false;
            return true;
        }

        /// <summary>
        /// Reads the members after the head into <paramref name="o" />. False when the object must be abandoned.
        /// </summary>
        private bool TryReadMembers(ReadAttempt attempt, WireNameMap members, ref object o)
        {
            WireKey[] keys = members.Keys;
            int hint = 0;
            bool ended = false;
            if (attempt.FirstMember != null)
                o = ReadMember(o, members.Find(attempt.FirstMember), attempt.Globaltypes, out ended);

            while (!ended)
            {
                byte t = _parser.ReadToken();
                if (t == Tokens.Comma)
                    continue;
                if (t == Tokens.DocEnd)
                    break;
                if (t == Tokens.TypesPointer)
                    return false;

                PropertyMetadata? pi = ReadMemberKey(t, members, keys, ref hint, out bool special);
                _parser.ReadColon();
                if (special)
                    return false;

                o = ReadMember(o, pi, attempt.Globaltypes, out ended);
            }

            return true;
        }

        /// <summary>
        /// Reads one member's value and applies it, as ParseDictionary's loop does for one entry.
        /// </summary>
        /// <param name="o">The object being filled.</param>
        /// <param name="pi">The member's metadata, or null when the key names no member.</param>
        /// <param name="globaltypes">The $types table in effect, if any.</param>
        /// <param name="ended">
        /// Set when the value position held a structural token instead - the parser ends the object
        /// there, and so does this.
        /// </param>
        /// <returns>The object that was filled.</returns>
        private object ReadMember(object o, PropertyMetadata? pi, Dictionary<string, object>? globaltypes, out bool ended)
        {
            ended = false;
            if (pi != null && pi.CanWrite)
            {
                if (TryReadStructuredMember(o, pi, globaltypes, out object filled))
                    return filled;

                if (pi.TypedSetter != null && TrySetTyped(o, pi))
                    return o;
            }

            object? v = _parser.ReadValue(out bool broke);
            if (broke)
            {
                ended = true;
                return o;
            }

            if (pi != null && pi.CanWrite && v != null)
                return pi.Setter!(o, _deserializer.ConvertValue(pi, v, globaltypes)!);

            return o;
        }

        /// <summary>
        /// Reads a plain object or list value straight into the member, when its shape allows it.
        /// </summary>
        private bool TryReadStructuredMember(object o, PropertyMetadata pi, Dictionary<string, object>? globaltypes, out object filled)
        {
            filled = o;
            byte next = _parser.PeekToken();
            if (next == Tokens.DocStart && IsPlainObjectMember(pi))
            {
                object? value;
                if (TryReadObject(pi.Pt, globaltypes, false, out object? read))
                    value = read;
                else
                    value = _deserializer.ConvertValue(pi, _parser.ReadValue(out _)!, globaltypes);

                filled = pi.Setter!(o, value!);
                return true;
            }

            if (next == Tokens.ArrayStart && IsPlainListMember(pi))
            {
                _parser.ReadToken();
                filled = pi.Setter!(o, ReadGenericList(pi.Pt, pi.Bt, globaltypes));
                return true;
            }

            return false;
        }

        /// <summary>
        /// Reads the head key of an object, whose token was just read. $type is recognised by its bytes.
        /// </summary>
        private string ReadHeadName(byte token)
        {
            int keyStart = _parser.Index - 1;
            if (_parser.TryReadKey(keyStart, TypeKeyUtf16) || _parser.TryReadKey(keyStart, TypeKeyUtf8))
                return WireKeys.Type;

            return _parser.ReadName(token);
        }

        /// <summary>
        /// Reads a member key whose token was just read: by its bytes when its type has seen it before,
        /// otherwise by decoding its name, after which it is remembered.
        /// </summary>
        /// <remarks>
        /// The search starts after the previous match, because a type's members arrive in the same
        /// order every time; a member skipped for being null costs one more comparison, not a miss.
        /// Equal bytes are the same name, so a remembered key resolves exactly as decoding it does.
        /// </remarks>
        private PropertyMetadata? ReadMemberKey(byte token, WireNameMap members, WireKey[] keys, ref int hint, out bool special)
        {
            int keyStart = _parser.Index - 1;
            for (int i = 0; i < keys.Length; i++)
            {
                int index = hint + i;
                if (index >= keys.Length)
                    index -= keys.Length;

                WireKey key = keys[index];
                if (_parser.TryReadKey(keyStart, key.Raw))
                {
                    hint = index + 1;
                    special = key.Special;
                    return key.Member;
                }
            }

            string name = _parser.ReadName(token);
            special = IsSpecialName(name);
            PropertyMetadata? member = special ? null : members.Find(name);
            // Only the one-byte length forms, so a remembered key never exceeds 257 bytes.
            if (token == Tokens.Name || token == Tokens.NameUtf16)
                members.Remember(_parser.CopyFrom(keyStart), member, special);

            return member;
        }

        private static byte[] Key(byte token, byte[] name)
        {
            byte[] key = new byte[name.Length + 2];
            key[0] = token;
            key[1] = (byte)name.Length;
            Buffer.BlockCopy(name, 0, key, 2, name.Length);
            return key;
        }

        /// <summary>
        /// Takes the $type at the current index from the previous one when its bytes are the same.
        /// </summary>
        /// <remarks>
        /// A root list is written without global types, so each element carries its full
        /// assembly-qualified type name, and they are mostly the same name. Comparing the bytes with
        /// the last one is far cheaper than decoding and hashing the name again. Only with no $types
        /// table in play: the name is then resolved the same way every time, so equal bytes are the
        /// same answer. A name that failed to resolve threw, and was never remembered.
        /// </remarks>
        private bool TryRepeatType(Dictionary<string, object>? globaltypes, out Type? type)
        {
            type = null;
            if (_lastTypeLength == 0 || (globaltypes != null && globaltypes.Count > 0))
                return false;
            if (!_parser.TrySkipRepeat(_lastTypeStart, _lastTypeLength))
                return false;

            type = _lastType;
            _deserializer.TypesRepeated++;
            return true;
        }

        private void RememberType(Dictionary<string, object>? globaltypes, int valueStart, Type? type)
        {
            if (globaltypes != null && globaltypes.Count > 0)
                return;

            _lastTypeStart = valueStart;
            _lastTypeLength = _parser.Index - valueStart;
            _lastType = type;
        }

        /// <summary>
        /// Resolves a string $type value without allocating its name. Returns false, having read
        /// nothing, when the value is not a string the cache can take - the caller reads it as before.
        /// </summary>
#if NET10_0_OR_GREATER
        private bool TryResolveTypeInPlace(Dictionary<string, object>? globaltypes, out Type? type)
        {
            // UTF-16 text is read in place and needs no buffer. The default encoding is UTF-16, and a
            // stackalloc is zeroed where it executes, so allocating one here for every $type cost a 1 KB
            // memset per object for nothing.
            if (_parser.PeekToken() == Tokens.Utf16String && _parser.TryReadStringChars(Span<char>.Empty, out ReadOnlySpan<char> inPlace))
            {
                type = TypeNameCache.Resolve(inPlace, globaltypes, _deserializer);
                _deserializer.TypesResolvedInPlace++;
                return true;
            }

            return TryResolveUtf8TypeInPlace(globaltypes, out type);
        }

        private bool TryResolveUtf8TypeInPlace(Dictionary<string, object>? globaltypes, out Type? type)
        {
            // Covers every assembly-qualified name in practice; a longer UTF-8 one takes the old path.
            Span<char> buffer = stackalloc char[512];
            if (_parser.TryReadStringChars(buffer, out ReadOnlySpan<char> name))
            {
                type = TypeNameCache.Resolve(name, globaltypes, _deserializer);
                _deserializer.TypesResolvedInPlace++;
                return true;
            }

            type = null;
            return false;
        }
#else
        // No span reader on this target, so the name is always read the long way.
        private static bool TryResolveTypeInPlace(Dictionary<string, object>? globaltypes, out Type? type)
        {
            _ = globaltypes;
            type = null;
            return false;
        }
#endif

        /// <summary>
        /// Sets a primitive member straight from its token, without boxing, when the token is the
        /// one the member's own type is written with. Returns false, having read nothing, otherwise.
        /// </summary>
        /// <remarks>
        /// That token is exactly the case in which the parser produces a value of the member's type,
        /// ConvertValue hands it through unchanged and the boxing setter's unbox.any succeeds - so
        /// the result is the same by construction. Every other token (null, another width, a value
        /// from an older or different writer) takes the boxing path and converts or throws there.
        /// </remarks>
        /// <exception cref="InvalidOperationException">If a member carries a token with no reader here.</exception>
        private bool TrySetTyped(object o, PropertyMetadata pi)
        {
            byte next = _parser.PeekToken();
            if (next != pi.TypedToken && (pi.TypedToken != Tokens.True || next != Tokens.False))
                return false;

            _parser.ReadToken();
            Delegate setter = pi.TypedSetter!;
            switch (next)
            {
                case Tokens.Int32:
                    ((TypeReflector.TypedSetter<int>)setter)(o, _parser.ParseInt());
                    break;
                case Tokens.Int64:
                    ((TypeReflector.TypedSetter<long>)setter)(o, _parser.ParseLong());
                    break;
                case Tokens.True:
                    ((TypeReflector.TypedSetter<bool>)setter)(o, true);
                    break;
                case Tokens.False:
                    ((TypeReflector.TypedSetter<bool>)setter)(o, false);
                    break;
                case Tokens.DateTime:
                    ((TypeReflector.TypedSetter<DateTime>)setter)(o, _parser.ParseDateTime());
                    break;
                case Tokens.Guid:
                    ((TypeReflector.TypedSetter<Guid>)setter)(o, _parser.ParseGuid());
                    break;
                case Tokens.Double:
                    ((TypeReflector.TypedSetter<double>)setter)(o, _parser.ParseDouble());
                    break;
                case Tokens.Single:
                    ((TypeReflector.TypedSetter<float>)setter)(o, _parser.ParseFloat());
                    break;
                case Tokens.Decimal:
                    ((TypeReflector.TypedSetter<decimal>)setter)(o, _parser.ParseDecimal());
                    break;
                case Tokens.Int16:
                    ((TypeReflector.TypedSetter<short>)setter)(o, _parser.ParseShort());
                    break;
                case Tokens.UInt16:
                    ((TypeReflector.TypedSetter<ushort>)setter)(o, _parser.ParseUShort());
                    break;
                case Tokens.UInt32:
                    ((TypeReflector.TypedSetter<uint>)setter)(o, _parser.ParseUint());
                    break;
                case Tokens.UInt64:
                    ((TypeReflector.TypedSetter<ulong>)setter)(o, _parser.ParseULong());
                    break;
                case Tokens.Byte:
                    ((TypeReflector.TypedSetter<byte>)setter)(o, _parser.ParseByte());
                    break;
                case Tokens.Char:
                    ((TypeReflector.TypedSetter<char>)setter)(o, _parser.ParseChar());
                    break;
                case Tokens.TimeSpan:
                    ((TypeReflector.TypedSetter<TimeSpan>)setter)(o, _parser.ParsTimeSpan());
                    break;
                case Tokens.DateTimeOffset:
                    ((TypeReflector.TypedSetter<DateTimeOffset>)setter)(o, _parser.ParseDateTimeOffset());
                    break;
                default:
                    throw new InvalidOperationException("No typed reader for token " + next + ".");
            }

            _deserializer.TypedSets++;
            return true;
        }

        /// <summary>
        /// The equivalent of CreateGenericList for the list whose ARRAY_START was just read.
        /// </summary>
        private object ReadGenericList(Type pt, Type? bt, Dictionary<string, object>? globaltypes)
        {
            List<object?> items = ReadElements(
                bt,
                globaltypes,
                false,
                ob =>
                {
                    if (ob is List<object> list)
                        return bt!.IsGenericType ? list : list.ToArray();
                    if (ob is TypedArray typed)
                        return typed.DataList.ToArray();

                    return ob;
                }
            );

            // Created after the elements are read, so it gets the capacity CreateGenericList gives it.
            IList col = (IList)TypeReflector.Instance.FastCreateList(pt, items.Count);
            foreach (object? item in items)
                col.Add(item);

            return col;
        }

        /// <summary>
        /// The equivalent of RootList for a root ARRAY_START.
        /// </summary>
        private object ReadRootList(Type type)
        {
            Type[] gtypes = TypeReflector.Instance.GetGenericArguments(type);
            // Shared by every element, as in RootList: the first element's $types table lands here.
            Dictionary<string, object> globals = new Dictionary<string, object>();

            _parser.ReadToken(); // ARRAY_START
            List<object?> items = ReadElements(gtypes[0], globals, true, ob => ob);

            IList o = (IList)TypeReflector.Instance.FastCreateList(type, items.Count);
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
            while (!broke)
            {
                byte t = ReadElement(items, bt, globaltypes, mayExtendSharedTypes, convert, out broke);
                if (t == Tokens.Comma)
                    continue;
                if (t == Tokens.ArrayEnd)
                    break;
            }

            return items;
        }

        /// <summary>
        /// Reads one element into <paramref name="items" /> and the token after it. When the value position held a
        /// structural token instead, nothing is added and that token is returned with <paramref name="broke" /> set.
        /// </summary>
        private byte ReadElement(
            List<object?> items,
            Type? bt,
            Dictionary<string, object>? globaltypes,
            bool mayExtendSharedTypes,
            Func<object?, object?> convert,
            out bool broke
        )
        {
            broke = false;

            // An object element is added as built, never through convert - as in both originals.
            if (_parser.PeekToken() == Tokens.DocStart)
            {
                if (!TryReadObject(bt, globaltypes, mayExtendSharedTypes, out object? read))
                    read = _deserializer.ParseDictionary((Dictionary<string, object>)_parser.ReadValue(out _)!, globaltypes, bt, null);

                items.Add(read);
                return _parser.ReadToken();
            }

            object? item = _parser.ReadValue(out broke);
            if (broke)
                return (byte)item!;

            items.Add(convert(item));
            return _parser.ReadToken();
        }

        private byte ReadSkippingCommas()
        {
            byte t = _parser.ReadToken();
            while (t == Tokens.Comma)
                t = _parser.ReadToken();

            return t;
        }

        // $type, $types, $i, $schema - anything the two-step path might treat specially.
        private static bool IsSpecialName(string name) => name.Length > 0 && name[0] == '$';

        private bool Abandon(ReadAttempt attempt)
        {
            _deserializer.OneStepFallbacks++;
            _deserializer.UndoCircular(attempt.Circular);
            if (attempt.SharedTypes != null && attempt.AddedTypes != null)
            {
                foreach (string key in attempt.AddedTypes)
                    attempt.SharedTypes.Remove(key);
            }

            _parser.Index = attempt.Start;
            return false;
        }

        private enum HeadOutcome
        {
            Continue,
            Reference,
            Abandoned,
        }

        /// <summary>
        /// What TryReadObject needs to undo if it gives up, and what it has learned about the object so far.
        /// </summary>
        private struct ReadAttempt
        {
            public ReadAttempt(int start, int circular, Dictionary<string, object>? globaltypes, Type? declared)
            {
                Start = start;
                Circular = circular;
                SharedTypes = globaltypes;
                Globaltypes = globaltypes;
                Type = declared;
                AddedTypes = null;
                FirstMember = null;
                ReadTypes = false;
                NeedsDeclaredType = true;
            }

            public int Start { get; }
            public int Circular { get; }
            public Dictionary<string, object>? SharedTypes { get; }
            public Dictionary<string, object>? Globaltypes { get; set; }
            public List<string>? AddedTypes { get; set; }
            public Type? Type { get; set; }
            public string? FirstMember { get; set; }
            public bool ReadTypes { get; set; }
            public bool NeedsDeclaredType { get; set; }
        }
    }
}
