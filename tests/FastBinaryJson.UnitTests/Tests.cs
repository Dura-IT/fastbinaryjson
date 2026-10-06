// ReSharper disable InconsistentNaming - ported upstream models and tests keep upstream's names
// ReSharper disable CollectionNeverQueried.Global - the serializer fills and reads these collections
// ReSharper disable PossibleNullReferenceException - ported assertions: a null result is the failure they report
// ReSharper disable AssignNullToNotNullAttribute - null input is the case under test
// ReSharper disable DefaultStructEqualityIsUsed.Global - a struct with default equality as a dictionary key is the case under test
// ReSharper disable UsageOfDefaultStructEquality - a struct with default equality as a dictionary key is the case under test
// ReSharper disable UnusedAutoPropertyAccessor.Global - reflection-only models: the serializer reads and writes these members, nothing calls them
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Data;
using System.Diagnostics;
using System.Dynamic;
using System.Globalization;
using System.Linq;
using System.Threading;
using DuraIT.FastBinaryJson;
using DuraIT.FastBinaryJson.Internal;
using NUnit.Framework;
using NUnit.Framework.Legacy;

// CS8981: every type declared in this ported file is all-lowercase (tests, colclass, baseclass,
// ...), a shape C# reserves for future language keywords. Renaming 24 types belongs to the
// restyle commit, not to the mechanical NUnit 2 -> NUnit 4 port, so the port stays a single-
// variable change. This pragma goes away when those types are renamed to PascalCase.
#pragma warning disable CS8981

// This ported file predates nullable reference types by a decade. The rest of the project is
// nullable-checked; annotating these 1,790 lines belongs to the restyle commit.
#nullable disable

/*
 * Four tests were removed from this file rather than ported further: Perftest, BigData,
 * Speed_Test_Serialize and Speed_Test_Deserialize (with their CreateLong and CreateBigdata
 * helpers).
 *
 * None of them asserted anything - they were DateTime.Now loops printing to the console, so they
 * could only fail by throwing. BigData built 2,000,000 objects from a clock-seeded Random and
 * peaked at 3.17 GB resident, six of the suite's eight seconds, which a 7 GB CI runner would have
 * carried on every push for no signal at all.
 *
 * They are not relocated, because benchmarks/FastBinaryJson.Benchmarks already measures the same
 * axes properly: BenchmarkDotNet, LaunchCount=3, MemoryDiagnoser, a fixed five-shape corpus and a
 * separate cold-start arm. A 2,000,000-element payload shape is the one thing here that corpus
 * does not cover; if it is wanted it belongs in PayloadFactory with a fixed seed, not in a unit
 * test.
 */

public class tests
{
    /*
     * Custom type registration is process-wide and there is no public way back, so three tests in
     * this file - CustomTypes, anonymoustype and the deleted datetimeoff - used to leave their
     * registration in place for whatever NUnit ran next. That decided results elsewhere: a
     * DateTimeOffset registered with a ToString()-based serializer loses sub-second precision, so
     * any later test round-tripping one natively would silently go through the lossy path, in an
     * order NUnit does not define.
     */
    [TearDown]
    public void ClearRegistrations()
    {
        TypeReflector.Instance.ClearCustomTypes();
    }

    #region [  helpers  ]
    static int thousandtimes = 1000;
    static int fivetimes = 5;

    public enum Gender
    {
        Male,
        Female,
    }

    public class colclass
    {
        public colclass()
        {
            items = new List<baseclass>();
            date = DateTime.Now;
            multilineString =
                @"
            AJKLjaskljLA
       ahjksjkAHJKS سلام فارسی
       AJKHSKJhaksjhAHSJKa
       AJKSHajkhsjkHKSJKash
       ASJKhasjkKASJKahsjk
            ";
            isNew = true;
            booleanValue = true;
            ordinaryDouble = 0.001;
            gender = Gender.Female;
            intarray = new[] { 1, 2, 3, 4, 5 };
        }

        public bool booleanValue { get; set; }
        public DateTime date { get; set; }
        public string multilineString { get; set; }
        public List<baseclass> items { get; set; }
        public decimal ordinaryDecimal { get; set; }
        public double ordinaryDouble { get; set; }
        public bool isNew { get; set; }
        public string laststring { get; set; }
        public Gender gender { get; set; }

        public DataSet dataset { get; set; }
        public Dictionary<string, baseclass> stringDictionary { get; set; }
        public Dictionary<baseclass, baseclass> objectDictionary { get; set; }
        public Dictionary<int, baseclass> intDictionary { get; set; }
        public Guid? nullableGuid { get; set; }
        public decimal? nullableDecimal { get; set; }
        public double? nullableDouble { get; set; }
        public Hashtable hash { get; set; }
        public baseclass[] arrayType { get; set; }
        public byte[] bytes { get; set; }
        public int[] intarray { get; set; }
    }

    public static colclass CreateObject(bool exotic, bool dataset)
    {
        var c = new colclass();

        c.booleanValue = true;
        c.ordinaryDecimal = 3;

        if (exotic)
        {
            c.nullableGuid = Guid.NewGuid();
            c.hash = new Hashtable();
            c.bytes = new byte[1024];
            c.stringDictionary = new Dictionary<string, baseclass>();
            c.objectDictionary = new Dictionary<baseclass, baseclass>();
            c.intDictionary = new Dictionary<int, baseclass>();
            c.nullableDouble = 100.003;

            if (dataset)
                c.dataset = CreateDataset();
            c.nullableDecimal = 3.14M;

            c.hash.Add(new class1("0", "hello", Guid.NewGuid()), new class2("1", "code", "desc"));
            c.hash.Add(new class2("0", "hello", "pppp"), new class1("1", "code", Guid.NewGuid()));

            c.stringDictionary.Add("name1", new class2("1", "code", "desc"));
            c.stringDictionary.Add("name2", new class1("1", "code", Guid.NewGuid()));

            c.intDictionary.Add(1, new class2("1", "code", "desc"));
            c.intDictionary.Add(2, new class1("1", "code", Guid.NewGuid()));

            c.objectDictionary.Add(new class1("0", "hello", Guid.NewGuid()), new class2("1", "code", "desc"));
            c.objectDictionary.Add(new class2("0", "hello", "pppp"), new class1("1", "code", Guid.NewGuid()));

            c.arrayType = new baseclass[2];
            c.arrayType[0] = new class1();
            c.arrayType[1] = new class2();
        }

        c.items.Add(new class1("1", "1", Guid.NewGuid()));
        c.items.Add(new class2("2", "2", "desc1"));
        c.items.Add(new class1("3", "3", Guid.NewGuid()));
        c.items.Add(new class2("4", "4", "desc2"));

        c.laststring = "" + DateTime.Now;

        return c;
    }

    public class baseclass
    {
        public string Name { get; set; }
        public string Code { get; set; }
    }

    public class class1 : baseclass
    {
        public class1() { }

        public class1(string name, string code, Guid g)
        {
            Name = name;
            Code = code;
            guid = g;
        }

        public Guid guid { get; set; }
    }

    public class class2 : baseclass
    {
        public class2() { }

        public class2(string name, string code, string desc)
        {
            Name = name;
            Code = code;
            description = desc;
        }

        public string description { get; set; }
    }

    public class NoExt
    {
        [System.Xml.Serialization.XmlIgnore()]
        public string Name { get; set; }
        public string Address { get; set; }
        public int Age { get; set; }
        public baseclass[] objs { get; set; }
        public Dictionary<string, class1> dic { get; set; }
        public NoExt intern { get; set; }
    }

    public class Retclass
    {
        public object ReturnEntity { get; set; }
        public string Name { get; set; }
        public string Field1;
        public int Field2;
#pragma warning disable CA1822, S2325 // A read-only INSTANCE property is the shape under test
        public string ppp
        {
            get { return "sdfas df "; }
        }
#pragma warning restore CA1822, S2325
        public DateTime date { get; set; }
        public DataTable ds { get; set; }
    }

    public struct Retstruct
    {
        public object ReturnEntity { get; set; }
        public string Name { get; set; }
        public string Field1;
        public int Field2;
#pragma warning disable CA1822, S2325 // A read-only INSTANCE property is the shape under test
        public string ppp
        {
            get { return "sdfas df "; }
        }
#pragma warning restore CA1822, S2325
        public DateTime date { get; set; }
        public DataTable ds { get; set; }
    }

    private static DataSet CreateDataset()
    {
        DataSet ds = new DataSet();
        for (int j = 1; j < 3; j++)
        {
            DataTable dt = new DataTable();
            dt.TableName = "Table" + j;
            dt.Columns.Add("col1", typeof(int));
            dt.Columns.Add("col2", typeof(string));
            dt.Columns.Add("col3", typeof(Guid));
            dt.Columns.Add("col4", typeof(string));
            dt.Columns.Add("col5", typeof(bool));
            dt.Columns.Add("col6", typeof(string));
            dt.Columns.Add("col7", typeof(string));
            ds.Tables.Add(dt);
            Random rrr = new Random();
            for (int i = 0; i < 100; i++)
            {
                DataRow dr = dt.NewRow();
                dr[0] = rrr.Next(int.MaxValue);
                dr[1] = "" + rrr.Next(int.MaxValue);
                dr[2] = Guid.NewGuid();
                dr[3] = "" + rrr.Next(int.MaxValue);
                dr[4] = true;
                dr[5] = "" + rrr.Next(int.MaxValue);
                dr[6] = "" + rrr.Next(int.MaxValue);

                dt.Rows.Add(dr);
            }
        }
        return ds;
    }

    public class RetNestedclass
    {
        public Retclass Nested { get; set; }
    }
    #endregion

    [Test]
    public static void objectarray()
    {
        var o = new object[] { 1, "sdfsdfs", DateTime.Now };
        var b = Bjson.ToBjson(o);
        var s = Bjson.ToObject(b) as object[];
        ClassicAssert.IsNotNull(s);
        ClassicAssert.AreEqual(3, s.Length);
        ClassicAssert.AreEqual(1, s[0]);
        ClassicAssert.AreEqual("sdfsdfs", s[1]);
    }

    [Test]
    public static void ClassTest()
    {
        Retclass r = new Retclass();
        r.Name = "hello";
        r.Field1 = "dsasdF";
        r.Field2 = 2312;
        r.date = DateTime.Now;
        using DataSet dataset = CreateDataset();
        r.ds = dataset.Tables[0];

        var b = Bjson.ToBjson(r);

        var o = Bjson.ToObject(b);

        ClassicAssert.AreEqual(2312, (o as Retclass).Field2);
    }

    [Test]
    public static void StructTest()
    {
        Retstruct r = new Retstruct();
        r.Name = "hello";
        r.Field1 = "dsasdF";
        r.Field2 = 2312;
        r.date = DateTime.Now;
        using DataSet dataset = CreateDataset();
        r.ds = dataset.Tables[0];

        var b = Bjson.ToBjson(r);

        var o = Bjson.ToObject(b);

        ClassicAssert.AreEqual(2312, ((Retstruct)o).Field2);
    }

    [Test]
    public static void ParseTest()
    {
        Retclass r = new Retclass();
        r.Name = "hello";
        r.Field1 = "dsasdF";
        r.Field2 = 2312;
        r.date = DateTime.Now;
        using DataSet dataset = CreateDataset();
        r.ds = dataset.Tables[0];

        var s = Bjson.ToBjson(r);

        var o = Bjson.Parse(s);

        ClassicAssert.IsNotNull(o);
    }

    [Test]
    public static void StringListTest()
    {
        List<string> ls = new List<string>();
        string[] letters = { "a", "b", "c", "d" };
        ls.AddRange(letters);

        var s = Bjson.ToBjson(ls);

        var o = Bjson.ToObject(s);

        ClassicAssert.IsNotNull(o);
    }

    [Test]
    public static void IntListTest()
    {
        List<int> ls = new List<int>();
        int[] numbers = { 1, 2, 3, 4, 5, 10 };
        ls.AddRange(numbers);

        var s = Bjson.ToBjson(ls);

        ClassicAssert.IsNotNull(Bjson.Parse(s));
        var o = Bjson.ToObject(s); // long[] {1,2,3,4,5,10}

        ClassicAssert.IsNotNull(o);
    }

    [Test]
    public static void Variables()
    {
        var s = Bjson.ToBjson(42);
        var o = Bjson.ToObject(s);
        ClassicAssert.AreEqual(42, o);

        s = Bjson.ToBjson("hello");
        o = Bjson.ToObject(s);
        ClassicAssert.AreEqual("hello", o);
    }

    [Test]
    public static void List_int()
    {
        List<int> ls = new List<int>();
        int[] numbers = { 1, 2, 3, 4, 5, 10 };
        ls.AddRange(numbers);

        var s = Bjson.ToBjson(ls);
        ClassicAssert.IsNotNull(Bjson.Parse(s));
        var o = Bjson.ToObject<List<int>>(s);

        ClassicAssert.IsNotNull(o);
    }

    [Test]
    public static void Dictionary_String_RetClass()
    {
        Dictionary<string, Retclass> r = new Dictionary<string, Retclass>();
        r.Add(
            "11",
            new Retclass
            {
                Field1 = "111",
                Field2 = 2,
                date = DateTime.Now,
            }
        );
        r.Add(
            "12",
            new Retclass
            {
                Field1 = "111",
                Field2 = 2,
                date = DateTime.Now,
            }
        );
        var s = Bjson.ToBjson(r);
        var o = Bjson.ToObject<Dictionary<string, Retclass>>(s);
        ClassicAssert.AreEqual(2, o.Count);
    }

    [Test]
    public static void Dictionary_String_RetClass_noextensions()
    {
        Dictionary<string, Retclass> r = new Dictionary<string, Retclass>();
        r.Add(
            "11",
            new Retclass
            {
                Field1 = "111",
                Field2 = 2,
                date = DateTime.Now,
            }
        );
        r.Add(
            "12",
            new Retclass
            {
                Field1 = "111",
                Field2 = 2,
                date = DateTime.Now,
            }
        );
        var s = Bjson.ToBjson(r, new BjsonParameters { UseExtensions = false });
        var o = Bjson.ToObject<Dictionary<string, Retclass>>(s);
        ClassicAssert.AreEqual(2, o.Count);
    }

    [Test]
    public static void Dictionary_int_RetClass()
    {
        Dictionary<int, Retclass> r = new Dictionary<int, Retclass>();
        r.Add(
            11,
            new Retclass
            {
                Field1 = "111",
                Field2 = 2,
                date = DateTime.Now,
            }
        );
        r.Add(
            12,
            new Retclass
            {
                Field1 = "111",
                Field2 = 2,
                date = DateTime.Now,
            }
        );
        var s = Bjson.ToBjson(r);
        var o = Bjson.ToObject<Dictionary<int, Retclass>>(s);
        ClassicAssert.AreEqual(2, o.Count);
    }

    [Test]
    public static void Dictionary_int_RetClass_noextensions()
    {
        Dictionary<int, Retclass> r = new Dictionary<int, Retclass>();
        r.Add(
            11,
            new Retclass
            {
                Field1 = "111",
                Field2 = 2,
                date = DateTime.Now,
            }
        );
        r.Add(
            12,
            new Retclass
            {
                Field1 = "111",
                Field2 = 2,
                date = DateTime.Now,
            }
        );
        var s = Bjson.ToBjson(r, new BjsonParameters { UseExtensions = false });
        var o = Bjson.ToObject<Dictionary<int, Retclass>>(s);
        ClassicAssert.AreEqual(2, o.Count);
    }

    [Test]
    public static void Dictionary_Retstruct_RetClass()
    {
        Dictionary<Retstruct, Retclass> r = new Dictionary<Retstruct, Retclass>();
        r.Add(
            new Retstruct
            {
                Field1 = "111",
                Field2 = 1,
                date = DateTime.Now,
            },
            new Retclass
            {
                Field1 = "111",
                Field2 = 2,
                date = DateTime.Now,
            }
        );
        r.Add(
            new Retstruct
            {
                Field1 = "222",
                Field2 = 2,
                date = DateTime.Now,
            },
            new Retclass
            {
                Field1 = "111",
                Field2 = 2,
                date = DateTime.Now,
            }
        );
        var s = Bjson.ToBjson(r);
        var o = Bjson.ToObject<Dictionary<Retstruct, Retclass>>(s);
        ClassicAssert.AreEqual(2, o.Count);
    }

    [Test]
    public static void Dictionary_Retstruct_RetClass_noextentions()
    {
        Dictionary<Retstruct, Retclass> r = new Dictionary<Retstruct, Retclass>();
        r.Add(
            new Retstruct
            {
                Field1 = "111",
                Field2 = 1,
                date = DateTime.Now,
            },
            new Retclass
            {
                Field1 = "111",
                Field2 = 2,
                date = DateTime.Now,
            }
        );
        r.Add(
            new Retstruct
            {
                Field1 = "222",
                Field2 = 2,
                date = DateTime.Now,
            },
            new Retclass
            {
                Field1 = "111",
                Field2 = 2,
                date = DateTime.Now,
            }
        );
        var s = Bjson.ToBjson(r, new BjsonParameters { UseExtensions = false });
        var o = Bjson.ToObject<Dictionary<Retstruct, Retclass>>(s);
        ClassicAssert.AreEqual(2, o.Count);
    }

    [Test]
    public static void List_RetClass()
    {
        List<Retclass> r = new List<Retclass>();
        r.Add(
            new Retclass
            {
                Field1 = "111",
                Field2 = 2,
                date = DateTime.Now,
            }
        );
        r.Add(
            new Retclass
            {
                Field1 = "222",
                Field2 = 3,
                date = DateTime.Now,
            }
        );
        var s = Bjson.ToBjson(r);
        var o = Bjson.ToObject<List<Retclass>>(s);
        ClassicAssert.AreEqual(2, o.Count);
    }

    [Test]
    public static void List_RetClass_noextensions()
    {
        List<Retclass> r = new List<Retclass>();
        r.Add(
            new Retclass
            {
                Field1 = "111",
                Field2 = 2,
                date = DateTime.Now,
            }
        );
        r.Add(
            new Retclass
            {
                Field1 = "222",
                Field2 = 3,
                date = DateTime.Now,
            }
        );
        var s = Bjson.ToBjson(r, new BjsonParameters { UseExtensions = false });
        var o = Bjson.ToObject<List<Retclass>>(s);
        ClassicAssert.AreEqual(2, o.Count);
    }

    [Test]
    public static void FillObject()
    {
        NoExt ne = new NoExt();
        ne.Name = "hello";
        ne.Address = "here";
        ne.Age = 10;
        ne.dic = new Dictionary<string, class1>();
        ne.dic.Add("hello", new class1("asda", "asdas", Guid.NewGuid()));
        ne.objs = new baseclass[] { new class1("a", "1", Guid.NewGuid()), new class2("b", "2", "desc") };

        byte[] str = Bjson.ToBjson(ne, new BjsonParameters { UseExtensions = false, UsingGlobalTypes = false });
        ClassicAssert.IsNotNull(Bjson.Parse(str));
        NoExt oo = Bjson.ToObject<NoExt>(str);
        ClassicAssert.AreEqual("here", oo.Address);

        NoExt nee = new NoExt();
        nee.intern = new NoExt { Name = "aaa" };
        Bjson.FillObject(nee, str);
        ClassicAssert.AreEqual("here", nee.Address);
        ClassicAssert.AreEqual(10, nee.Age);
    }

    [Test]
    public static void AnonymousTypes()
    {
        Console.WriteLine(".net version = " + Environment.Version);
        var q = new
        {
            Name = "asassa",
            Address = "asadasd",
            Age = 12,
        };
        byte[] sq = Bjson.ToBjson(q, new BjsonParameters { EnableAnonymousTypes = true });
        ClassicAssert.IsNotEmpty(sq);
    }

    [Test]
    public static void List_NestedRetClass()
    {
        List<RetNestedclass> r = new List<RetNestedclass>();
        r.Add(
            new RetNestedclass
            {
                Nested = new Retclass
                {
                    Field1 = "111",
                    Field2 = 2,
                    date = DateTime.Now,
                },
            }
        );
        r.Add(
            new RetNestedclass
            {
                Nested = new Retclass
                {
                    Field1 = "222",
                    Field2 = 3,
                    date = DateTime.Now,
                },
            }
        );
        var s = Bjson.ToBjson(r);
        var o = Bjson.ToObject<List<RetNestedclass>>(s);
        ClassicAssert.AreEqual(2, o.Count);
    }

    [Test]
    public static void NullTest()
    {
        var s = Bjson.ToBjson(null);
        ClassicAssert.AreEqual(Tokens.Null, s[0]);
        var o = Bjson.ToObject(s);
        ClassicAssert.AreEqual(null, o);
    }

    [Test]
    public static void ZeroArray()
    {
        var s = Bjson.ToBjson(Array.Empty<object>());
        var o = Bjson.ToObject(s);
        var a = o as object[];
        ClassicAssert.AreEqual(0, a.Length);
    }

    [Test]
    public static void GermanNumbers()
    {
        // Upstream restored to a hardcoded "en" rather than to whatever was there, and did it
        // outside any try/finally - so a failed assertion left the whole rest of the run in "de",
        // and a passing one left it in "en" regardless of the machine's real locale. Either way
        // later culture-sensitive tests stopped testing the culture they appeared to.
        var original = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new CultureInfo("de");
            decimal d = 3.141592654M;
            var s = Bjson.ToBjson(d);
            var o = Bjson.ToObject(s);
            ClassicAssert.AreEqual(d, (decimal)o);
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = original;
        }
    }

    public class arrayclass
    {
        public int[] ints { get; set; }
        public string[] strs;
    }

    [Test]
    public static void ArrayTest()
    {
        arrayclass a = new arrayclass();
        a.ints = new[] { 3, 1, 4 };
        a.strs = new[] { "a", "b", "c" };
        var s = Bjson.ToBjson(a);
        var o = (arrayclass)Bjson.ToObject(s);
        CollectionAssert.AreEqual(a.ints, o.ints);
        CollectionAssert.AreEqual(a.strs, o.strs);
    }

    [Test]
    public static void Datasets()
    {
        using DataSet ds = CreateDataset();

        var s = Bjson.ToBjson(ds);

        var o = Bjson.ToObject<DataSet>(s);

        ClassicAssert.AreEqual(typeof(DataSet), o.GetType());
        ClassicAssert.IsNotNull(o);
        ClassicAssert.AreEqual(2, o.Tables.Count);

        s = Bjson.ToBjson(ds.Tables[0]);
        var oo = Bjson.ToObject<DataTable>(s);
        ClassicAssert.IsNotNull(oo);
        ClassicAssert.AreEqual(typeof(DataTable), oo.GetType());
        ClassicAssert.AreEqual(100, oo.Rows.Count);
    }

    [Test]
    public static void DynamicTest()
    {
        var obj = new
        {
            Name = "aaaaaa",
            Age = 10,
            dob = DateTime.Parse("2000-01-01 00:00:00", CultureInfo.InvariantCulture),
            inner = new { prop = 30 },
        };

        byte[] b = Bjson.ToBjson(obj, new BjsonParameters { UseExtensions = false, EnableAnonymousTypes = true });
        dynamic d = Bjson.ToDynamic(b);
        var ss = d.Name;
        var oo = d.Age;
        var dob = d.dob;
        var inp = d.inner.prop;

        ClassicAssert.AreEqual("aaaaaa", ss);
        ClassicAssert.AreEqual(10, oo);
        ClassicAssert.AreEqual(30, inp);
        ClassicAssert.AreEqual(DateTime.Parse("2000-01-01 00:00:00", CultureInfo.InvariantCulture), dob);
    }

    public class diclist
    {
        public Dictionary<string, List<string>> d;
    }

    [Test]
    public static void DictionaryWithListValue()
    {
        diclist dd = new diclist();
        dd.d = new Dictionary<string, List<string>>();
        dd.d.Add("a", new List<string> { "1", "2", "3" });
        dd.d.Add("b", new List<string> { "4", "5", "7" });
        byte[] s = Bjson.ToBjson(dd, new BjsonParameters { UseExtensions = false });
        var o = Bjson.ToObject<diclist>(s);
        ClassicAssert.AreEqual(3, o.d["a"].Count);

        s = Bjson.ToBjson(dd.d, new BjsonParameters { UseExtensions = false });
        var oo = Bjson.ToObject<Dictionary<string, List<string>>>(s);
        ClassicAssert.AreEqual(3, oo["a"].Count);
        var ooo = Bjson.ToObject<Dictionary<string, string[]>>(s);
        ClassicAssert.AreEqual(3, ooo["b"].Length);
    }

    [Test]
    public static void HashtableTest()
    {
        Hashtable h = new Hashtable();
        h.Add(1, "dsjfhksa");
        h.Add("dsds", new class1());

        var s = Bjson.ToBjson(h);

        var o = Bjson.ToObject<Hashtable>(s);
        ClassicAssert.AreEqual(typeof(Hashtable), o.GetType());
        ClassicAssert.AreEqual(typeof(class1), o["dsds"].GetType());
    }

    public class coltest
    {
        public string name;
        public NameValueCollection nv;
        public StringDictionary sd;
    }

    [Test]
    public static void SpecialCollections()
    {
        var nv = new NameValueCollection();
        nv.Add("1", "a");
        nv.Add("2", "b");
        var s = Bjson.ToBjson(nv);
        var oo = Bjson.ToObject<NameValueCollection>(s);
        ClassicAssert.AreEqual("a", oo["1"]);
        var sd = new StringDictionary();
        sd.Add("1", "a");
        sd.Add("2", "b");
        s = Bjson.ToBjson(sd);
        var o = Bjson.ToObject<StringDictionary>(s);
        ClassicAssert.AreEqual("b", o["2"]);

        coltest c = new coltest();
        c.name = "aaa";
        c.nv = nv;
        c.sd = sd;
        s = Bjson.ToBjson(c);
        var ooo = Bjson.ToObject(s);
        ClassicAssert.AreEqual("a", (ooo as coltest).nv["1"]);
        ClassicAssert.AreEqual("b", (ooo as coltest).sd["2"]);
    }

    public enum enumt
    {
        None = 0,
        A = 65,
        B = 90,
        C = 100,
    }

    public class constch
    {
        public enumt e = enumt.B;
        public string Name = "aa";
        public const int age = 11;
    }

    [Test]
    public static void consttest()
    {
        var s = Bjson.ToBjson(new constch());
        var o = (constch)Bjson.ToObject(s);
        ClassicAssert.AreEqual("aa", o.Name);
        ClassicAssert.AreEqual(enumt.B, o.e);
    }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public sealed class IgnoreMarkerAttribute : Attribute { }

    public class ignore
    {
        public string Name { get; set; }

        [System.Xml.Serialization.XmlIgnore]
        public int Age1 { get; set; }

        [IgnoreMarker]
        public int Age2;
    }

#pragma warning disable S2094 // An empty derived type is the case under test
    public class ignore1 : ignore { }
#pragma warning restore S2094

    [Test]
    public static void IgnoreAttributes()
    {
        var i = new ignore
        {
            Age1 = 10,
            Age2 = 20,
            Name = "aa",
        };
        var s = Bjson.ToBjson(i);
        var o = Bjson.ToObject<ignore>(s);
        ClassicAssert.AreEqual(0, o.Age1);
        i = new ignore1
        {
            Age1 = 10,
            Age2 = 20,
            Name = "bb",
        };
        var j = new BjsonParameters();
        j.IgnoreAttributes.Add(typeof(IgnoreMarkerAttribute));
        s = Bjson.ToBjson(i, j);
        var oo = Bjson.ToObject<ignore1>(s);
        ClassicAssert.AreEqual(0, oo.Age1);
        ClassicAssert.AreEqual(0, oo.Age2);
    }

    public class nondefaultctor
    {
        public nondefaultctor(int a)
        {
            age = a;
        }

        public int age;
    }

    [Test]
    public static void NonDefaultConstructor()
    {
        var o = new nondefaultctor(10);
        var s = Bjson.ToBjson(o);
        var obj = Bjson.ToObject<nondefaultctor>(s, new BjsonParameters { ParametricConstructorOverride = true });
        ClassicAssert.AreEqual(10, obj.age);
        List<nondefaultctor> l = new List<nondefaultctor> { o, o, o };
        s = Bjson.ToBjson(l);
        var obj2 = Bjson.ToObject<List<nondefaultctor>>(s, new BjsonParameters { ParametricConstructorOverride = true });
        ClassicAssert.AreEqual(3, obj2.Count);
        ClassicAssert.AreEqual(10, obj2[1].age);
    }

    public class o1
    {
        public int o1int;
        public o2 o2obj;
        public o3 child;
    }

    public class o2
    {
        public int o2int;
        public o1 parent;
    }

    public class o3
    {
        public int o3int;
        public o2 child;
    }

    [Test]
    public static void CircularReferences()
    {
        var o = new o1
        {
            o1int = 1,
            child = new o3 { o3int = 3 },
            o2obj = new o2 { o2int = 2 },
        };
        o.o2obj.parent = o;
        o.child.child = o.o2obj;

        var s = Bjson.ToBjson(o, new BjsonParameters());
        var p = Bjson.ToObject<o1>(s);
        ClassicAssert.AreEqual(p, p.o2obj.parent);
        ClassicAssert.AreEqual(p.o2obj, p.child.child);
    }

    public class lol
    {
        public List<List<object>> r;
    }

    public class lol2
    {
        public List<object[]> r;
    }

    [Test]
    public static void ListOfList()
    {
        var o = new List<List<object>>
        {
            new List<object> { 1, 2, 3 },
            new List<object> { "aa", 3, "bb" },
        };
        var s = Bjson.ToBjson(o);
        ClassicAssert.IsNotNull(Bjson.ToObject(s));
        var p = new lol { r = o };
        s = Bjson.ToBjson(p);
        var i = Bjson.ToObject(s);
        ClassicAssert.AreEqual(3, (i as lol).r[0].Count);

        var oo = new List<object[]> { new object[] { 1, 2, 3 }, new object[] { "a", 4, "b" } };
        s = Bjson.ToBjson(oo);
        ClassicAssert.IsNotNull(Bjson.ToObject(s));
        lol2 l = new lol2() { r = oo };

        s = Bjson.ToBjson(l);
        var iii = Bjson.ToObject(s);
        ClassicAssert.AreEqual(3, (iii as lol2).r[0].Length);
    }

    public class Y
    {
        public byte[] BinaryData;
    }

    public class A
    {
        public int DataA;
        public A NextA;
    }

    public class B : A
    {
        public string DataB;
    }

    public class C : A
    {
        public DateTime DataC;
    }

    public class Root
    {
        public Y TheY;
        public List<A> ListOfAs = new List<A>();
        public string UnicodeText;
        public Root NextRoot;
        public int MagicInt { get; set; }
        public A TheReferenceA;

        public void SetMagicInt(int value)
        {
            MagicInt = value;
        }
    }

    [Test]
    public static void complexobject()
    {
        Root r = new Root();
        r.TheY = new Y { BinaryData = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF } };
        r.ListOfAs.Add(new A { DataA = 10 });
        r.ListOfAs.Add(new B { DataA = 20, DataB = "Hello" });
        r.ListOfAs.Add(new C { DataA = 30, DataC = DateTime.Today });
        r.UnicodeText = "Žlutý kůň ∊ WORLD";
        r.ListOfAs[2].NextA = r.ListOfAs[1];
        r.ListOfAs[1].NextA = r.ListOfAs[2];
        r.TheReferenceA = r.ListOfAs[2];
        r.NextRoot = r;

        Root back = Bjson.ToObject<Root>(Bjson.ToBjson(r));

        ClassicAssert.AreEqual("Žlutý kůň ∊ WORLD", back.UnicodeText);
        ClassicAssert.AreEqual(3, back.ListOfAs.Count);
        ClassicAssert.AreSame(back, back.NextRoot);
    }

    public struct Foo
    {
        public string name;
    };

    public class Bar
    {
        public Foo foo;
    };

    [Test]
    public static void StructProperty()
    {
        Bar b = new Bar();
        b.foo = new Foo();
        b.foo.name = "Buzz";
        var json = Bjson.ToBjson(b);
        Bar bar = Bjson.ToObject<Bar>(json);
        ClassicAssert.AreEqual("Buzz", bar.foo.name);
    }

    public class readonlyclass
    {
        public readonlyclass()
        {
            ROName = "bb";
            Age = 10;
        }

        private readonly string _ro = "aa";
        public string ROAddress
        {
            get { return _ro; }
        }
        public string ROName { get; private set; }
        public int Age { get; set; }
    }

    [Test]
    public static void ReadonlyTest()
    {
        var s = Bjson.ToBjson(new readonlyclass(), new BjsonParameters { ShowReadOnlyProperties = true });
        var o = Bjson.ToObject(s);
        ClassicAssert.IsInstanceOf<readonlyclass>(o);
    }

    public class InstrumentSettings
    {
        public string dataProtocol { get; set; }
        public static bool isBad { get; set; }
        public static bool isOk;

        public InstrumentSettings()
        {
            dataProtocol = "Wireless";
        }
    }

    [Test]
    public static void statictest()
    {
        var s = new InstrumentSettings();
        BjsonParameters pa = new BjsonParameters();
        pa.UseExtensions = false;
        InstrumentSettings.isOk = true;
        InstrumentSettings.isBad = true;

        var jsonStr = Bjson.ToBjson(s, pa);

        var o = Bjson.ToObject<InstrumentSettings>(jsonStr);
        ClassicAssert.AreEqual("Wireless", o.dataProtocol);
    }

    public class arrayclass2
    {
        public int[] ints { get; set; }
        public string[] strs;
        public int[][] int2d { get; set; }
        public int[][][] int3d;
        public baseclass[][] class2d;
    }

    [Test]
    public static void ArrayTest2()
    {
        arrayclass2 a = new arrayclass2();
        a.ints = new[] { 3, 1, 4 };
        a.strs = new[] { "a", "b", "c" };
        a.int2d = new[] { new[] { 1, 2, 3 }, new[] { 2, 3, 4 } };
        a.int3d = new[] { new[] { new[] { 0, 0, 1 }, new[] { 0, 1, 0 } }, null, new[] { new[] { 0, 0, 2 }, new[] { 0, 2, 0 }, null } };
        a.class2d = new[]
        {
            new[]
            {
                new baseclass() { Name = "a", Code = "A" },
                new baseclass() { Name = "b", Code = "B" },
            },
            new[] { new baseclass() { Name = "c" } },
            null,
        };
        var s = Bjson.ToBjson(a);
        var o = Bjson.ToObject<arrayclass2>(s);
        CollectionAssert.AreEqual(a.ints, o.ints);
        CollectionAssert.AreEqual(a.strs, o.strs);
        CollectionAssert.AreEqual(a.int2d[0], o.int2d[0]);
        CollectionAssert.AreEqual(a.int2d[1], o.int2d[1]);
        CollectionAssert.AreEqual(a.int3d[0][0], o.int3d[0][0]);
        CollectionAssert.AreEqual(a.int3d[0][1], o.int3d[0][1]);
        ClassicAssert.AreEqual(null, o.int3d[1]);
        CollectionAssert.AreEqual(a.int3d[2][0], o.int3d[2][0]);
        CollectionAssert.AreEqual(a.int3d[2][1], o.int3d[2][1]);
        CollectionAssert.AreEqual(a.int3d[2][2], o.int3d[2][2]);
        for (int i = 0; i < a.class2d.Length; i++)
        {
            var ai = a.class2d[i];
            var oi = o.class2d[i];
            if (ai == null && oi == null)
            {
                continue;
            }
            for (int j = 0; j < ai.Length; j++)
            {
                var aii = ai[j];
                var oii = oi[j];
                if (aii == null && oii == null)
                {
                    continue;
                }
                ClassicAssert.AreEqual(aii.Name, oii.Name);
                ClassicAssert.AreEqual(aii.Code, oii.Code);
            }
        }
    }

    [Test]
    public static void Dictionary_String_Object_WithList()
    {
        Dictionary<string, object> dict = new Dictionary<string, object>();

        dict.Add("C", new List<float>() { 1.1f, 2.2f, 3.3f });
        var json = Bjson.ToBjson(dict);

        var des = Bjson.ToObject<Dictionary<string, List<float>>>(json);
        ClassicAssert.IsInstanceOf<List<float>>(des["C"]);
    }

    [Test]
    public static void exotic_deserialize()
    {
        Console.WriteLine();
#pragma warning disable CA1303 // Timing smoke test progress text; nothing here is localised
        Console.Write("fastbinaryjson deserialize");
#pragma warning restore CA1303
        colclass c = CreateObject(true, true);
        var stopwatch = new Stopwatch();
        for (int pp = 0; pp < fivetimes; pp++)
        {
            colclass deserializedStore;
            byte[] jsonText;

            stopwatch.Restart();
            jsonText = Bjson.ToBjson(c);
            for (int i = 0; i < thousandtimes; i++)
            {
                deserializedStore = (colclass)Bjson.ToObject(jsonText);
                ClassicAssert.IsNotNull(deserializedStore);
            }
            stopwatch.Stop();
            Console.Write("\t" + stopwatch.ElapsedMilliseconds);
        }
    }

    [Test]
    public static void exotic_serialize()
    {
        Console.WriteLine();
#pragma warning disable CA1303 // Timing smoke test progress text; nothing here is localised
        Console.Write("fastbinaryjson serialize");
#pragma warning restore CA1303
        colclass c = CreateObject(true, true);
        var stopwatch = new Stopwatch();
        for (int pp = 0; pp < fivetimes; pp++)
        {
            byte[] jsonText = null;
            stopwatch.Restart();
            for (int i = 0; i < thousandtimes; i++)
            {
                jsonText = Bjson.ToBjson(c);
            }
            ClassicAssert.IsNotEmpty(jsonText);
            stopwatch.Stop();
            Console.Write("\t" + stopwatch.ElapsedMilliseconds);
        }
    }

    public class ctype
    {
        public System.Net.IPAddress ip;
    }

    [Test]
    public static void CustomTypes()
    {
        var ip = new ctype();
        // Back to upstream's `IPAddress.Loopback`. It had to be replaced with a directly
        // constructed address for a while: on .NET that static returns a private
        // System.Net.IPAddress+ReadOnlyIPAddress subclass, and registration used to match the exact
        // runtime type, so the registration below was skipped and reflection reached
        // IPAddress.ScopeId, which throws SocketException for any IPv4 address. Registration now
        // resolves through the base chain, so the original line works again - see
        // RoundTrip.CustomTypeTests.
        ip.ip = System.Net.IPAddress.Loopback;

        Bjson.RegisterCustomType(
            typeof(System.Net.IPAddress),
            (x) =>
            {
                return x.ToString();
            },
            (x) =>
            {
                return System.Net.IPAddress.Parse(x);
            }
        );

        var s = Bjson.ToBjson(ip);

        var o = Bjson.ToObject<ctype>(s);
        ClassicAssert.AreEqual(ip.ip, o.ip);
    }

    public class readonlyProps
    {
        public List<string> Collection { get; }

        public readonlyProps(List<string> collection)
        {
            Collection = collection;
        }

        public readonlyProps() { }
    }

    [Test]
    public static void ReadOnlyProperty() // rbeurskens
    {
        var dto = new readonlyProps(new List<string> { "test", "test2" });

        // Upstream set Bjson.Parameters.ShowReadOnlyProperties and never put it back. That is a
        // process-wide singleton, so every later test using a parameterless Bjson call ran under
        // the mutated value, with no ordering guarantee about which ones. Passed explicitly here
        // instead, which leaves the global untouched.
        var parameters = new BjsonParameters { ShowReadOnlyProperties = true };
        var s = Bjson.ToBjson(dto, parameters);
        var o = Bjson.ToObject<readonlyProps>(s, parameters);

        ClassicAssert.IsNotNull(o);
        CollectionAssert.AreEqual(dto.Collection, o.Collection);
    }

    [Test]
    public static void anonymoustype()
    {
        var jsonParameters = new BjsonParameters { EnableAnonymousTypes = true };
        Bjson.RegisterCustomType(
            typeof(DateTimeOffset),
            (x) =>
            {
                return x.ToString();
            },
            (x) =>
            {
                return DateTimeOffset.Parse(x, CultureInfo.InvariantCulture);
            }
        );
        var data = new List<DateTimeOffset>();
        data.Add(new DateTimeOffset(DateTime.Now));

        var anonTypeWithDateTimeOffset = data.Select(entry => new { DateTimeOffset = entry }).ToList();
        var json = Bjson.ToBjson(anonTypeWithDateTimeOffset[0], jsonParameters);
        ClassicAssert.IsNotEmpty(json);

        var obj = new
        {
            Name = "aa",
            Age = 42,
            Code = "007",
        };

        json = Bjson.ToBjson(obj, jsonParameters);
        var p = Bjson.Parse(json);
        ClassicAssert.True((p as Dictionary<string, object>).ContainsKey("Name"));
        Bjson.ClearReflectionCache();
    }

    [Test]
    public static void Expando()
    {
        dynamic obj = new ExpandoObject();
        obj.UserView = "10080";
        obj.UserCatalog = "test";
        obj.UserDate = DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;
        obj.UserBase = "";

        var s = Bjson.ToBjson(obj);
        var p = Bjson.Parse(s);
        ClassicAssert.True((p as Dictionary<string, object>).ContainsKey("UserView"));
    }

    [Test]
    public static void NaN()
    {
        double d = double.NaN;
        float f = float.NaN;

        var s = Bjson.ToBjson(d);
        var o = Bjson.ToObject<double>(s);
        ClassicAssert.AreEqual(d, o);

        s = Bjson.ToBjson(f);
        var oo = Bjson.ToObject<float>(s);
        ClassicAssert.AreEqual(f, oo);

        var pp = Bjson.ToObject<Single>(s);
        ClassicAssert.AreEqual(f, pp);
    }

    [Test]
    public static void nonstandardkey()
    {
        Dictionary<string, object> dict = new Dictionary<string, object>();
        dict["With \"Quotes\""] = "With \"Quotes\"";
        BjsonParameters p = new BjsonParameters();
        p.EnableAnonymousTypes = false;
        p.UseExtensions = false;
        var s = Bjson.ToBjson(dict, p);
        var d = Bjson.ToObject<Dictionary<string, string>>(s);
        ClassicAssert.AreEqual(1, d.Count);
        ClassicAssert.AreEqual("With \"Quotes\"", d.Keys.First());
    }

    [Test]
    public static void ByteArrayInDictionary()
    {
        var s = Bjson.ToBjson(new Dictionary<string, byte[]> { { "Test", new byte[10] }, { "Test 2", Array.Empty<byte>() } });

        var d = Bjson.ToObject<Dictionary<string, byte[]>>(s);
        ClassicAssert.AreEqual(typeof(byte[]), d["Test 2"].GetType());
    }

    public class X
    {
        private readonly int i;

        public X(int i)
        {
            this.i = i;
        }

        public int I
        {
            get { return this.i; }
        }
    }

    [Test]
    public static void ReadonlyProperty()
    {
        var x = new X(10);
        var s = Bjson.ToBjson(x, new BjsonParameters { ShowReadOnlyProperties = true });
        var b = Bjson.Parse(s);
        ClassicAssert.True((b as Dictionary<string, object>).ContainsKey("I"));
        var o = Bjson.ToObject<X>(s, new BjsonParameters { ParametricConstructorOverride = true });
        // no set available -> I = 0
        ClassicAssert.AreEqual(0, o.I);
    }

    public class il
    {
        public IList list { get; set; }
        public string name;
    }

    [Test]
    public static void ilist()
    {
        var i = new il();
        i.list = new List<baseclass>();
        i.list.Add(new class1("1", "1", Guid.NewGuid()));
        i.list.Add(new class2("4", "5", "hi"));
        i.name = "hi";

        var s = Bjson.ToBjson(i);

        var o = Bjson.ToObject<il>(s);
        ClassicAssert.AreEqual("hi", o.name);
        ClassicAssert.AreEqual(2, o.list.Count);
    }

    public interface iintfc
    {
        string name { get; set; }
        int age { get; set; }
    }

    public class intfc : iintfc
    {
        public string address = "fadfsdf";
        public int age { get; set; }
        public string name { get; set; }
    }

    public class it
    {
        public iintfc i { get; set; }
        public string name = "bb";
    }

    [Test]
    public static void interface_test()
    {
        var ii = new it();

        var i = new intfc();
        i.age = 10;
        i.name = "aa";

        ii.i = i;

        var s = Bjson.ToBjson(ii);

        var o = (it)Bjson.ToObject(s);
        ClassicAssert.AreEqual(10, o.i.age);
        ClassicAssert.AreEqual("aa", o.i.name);
    }

    [Test]
    public static void nested_dictionary()
    {
        var dic = new Dictionary<int, Dictionary<string, double>>();
        dic.Add(0, new Dictionary<string, double> { { "PX_LAST", 1.1 }, { "PX_LOW", 1.0 } });
        dic.Add(1, new Dictionary<string, double> { { "PX_LAST", 2.1 }, { "PX_LOW", 2.0 } });

        var s = Bjson.ToBjson(dic);
        var obj = Bjson.ToObject<Dictionary<int, Dictionary<string, double>>>(s);
        ClassicAssert.AreEqual(2, obj[0].Count);
    }

    public class dyen
    {
        public string Prop1;
        public string Prop2;
    }

    [Test]
    public static void DynamicEnumerate()
    {
        var oo = new[]
        {
            new dyen { Prop1 = "1111", Prop2 = "2222" },
            new dyen { Prop1 = "11111", Prop2 = "22222" },
        };

        var j = Bjson.ToBjson(oo);

        var testObject = Bjson.ToDynamic(j);
        foreach (var o in testObject)
        {
            Console.WriteLine(o.Prop1);
            ClassicAssert.True(o.Prop1 != "");
        }
    }

    public class objcontainer
    {
        public object ds;
    }

    [Test]
    public static void objectasdataset()
    {
        var o = new objcontainer();
        o.ds = CreateDataset();

        var s = Bjson.ToBjson(o);

        var r = Bjson.ToObject<objcontainer>(s);
        Console.WriteLine("" + r.ds.GetType());
        ClassicAssert.True(r.ds.GetType() == typeof(DataSet));
    }

    public class simpclass
    {
        public int[] ints = new[] { 1, 2 };
        public string name = "aa";
        public long age = 42;
    }

    [Test]
    public static void TypedArrays()
    {
        var o = new objcontainer();
        o.ds = new[] { new simpclass(), new simpclass() };
        var s = Bjson.ToBjson(o);

        var r = Bjson.ToObject<objcontainer>(s);
        ClassicAssert.True(typeof(simpclass[]) == r.ds.GetType());

        // value type array as root
        var ii = new[] { 1, 2, 3, 4, 5 };
        s = Bjson.ToBjson(ii);
        var rr = Bjson.ToObject<int[]>(s);
        ClassicAssert.True(typeof(int[]) == rr.GetType());
    }

    [Test]
    public static void Timespan()
    {
        TimeSpan ts = new TimeSpan(2, 2, 2, 2);
        var b = Bjson.ToBjson(ts);
        var o = Bjson.ToObject<TimeSpan>(b);
        ClassicAssert.AreEqual(ts, o);
    }

    public class DigitLimit
    {
        public float Fmin;
        public float Fmax;
        public decimal MminDec;
        public decimal MmaxDec;

        public decimal Mmin;
        public decimal Mmax;
        public double Dmin;
        public double Dmax;
        public double DminDec;
        public double DmaxDec;
        public double Dni;
        public double Dpi;
        public double Dnan;
        public float FminDec;
        public float FmaxDec;
        public float Fni;
        public float Fpi;
        public float Fnan;
        public long Lmin;
        public long Lmax;
        public ulong ULmax;
        public int Imin;
        public int Imax;
        public uint UImax;

        //public IntPtr Iptr1 = new IntPtr(0); //Serialized to a Dict, exception on deserialization
        //public IntPtr Iptr2 = new IntPtr(0x33445566); //Serialized to a Dict, exception on deserialization
        //public UIntPtr UIptr1 = new UIntPtr(0); //Serialized to a Dict, exception on deserialization
        //public UIntPtr UIptr2 = new UIntPtr(0x55667788); //Serialized to a Dict, exception on deserialization
    }

    [Test]
    public static void digitlimits()
    {
        var d = new DigitLimit();
        d.Fmin = float.MinValue; // serializer loss on tostring()
        d.Fmax = float.MaxValue; // serializer loss on tostring()
        d.MminDec = -7.9228162514264337593543950335m; //OK to be serialized but lost precision in deserialization
        d.MmaxDec = +7.9228162514264337593543950335m; //OK to be serialized but lost precision in deserialization

        d.Mmin = decimal.MinValue;
        d.Mmax = decimal.MaxValue;
        d.Dmin = double.MinValue;
        d.Dmax = double.MaxValue;
        d.DminDec = -double.Epsilon;
        d.DmaxDec = double.Epsilon;
        d.Dni = double.NegativeInfinity;
        d.Dpi = double.PositiveInfinity;
        d.Dnan = double.NaN;
        d.FminDec = -float.Epsilon;
        d.FmaxDec = float.Epsilon;
        d.Fni = float.NegativeInfinity;
        d.Fpi = float.PositiveInfinity;
        d.Fnan = float.NaN;
        d.Lmin = long.MinValue;
        d.Lmax = long.MaxValue;
        d.ULmax = ulong.MaxValue;
        d.Imin = int.MinValue;
        d.Imax = int.MaxValue;
        d.UImax = uint.MaxValue;

        var s = Bjson.ToBjson(d);
        Console.WriteLine(s);
        var o = Bjson.ToObject<DigitLimit>(s);

        //ok
        ClassicAssert.AreEqual(d.Dmax, o.Dmax);
        ClassicAssert.AreEqual(d.DmaxDec, o.DmaxDec);
        ClassicAssert.AreEqual(d.Dmin, o.Dmin);
        ClassicAssert.AreEqual(d.DminDec, o.DminDec);
        ClassicAssert.AreEqual(d.Dnan, o.Dnan);
        ClassicAssert.AreEqual(d.Dni, o.Dni);
        ClassicAssert.AreEqual(d.Dpi, o.Dpi);
        ClassicAssert.AreEqual(d.FmaxDec, o.FmaxDec);
        ClassicAssert.AreEqual(d.FminDec, o.FminDec);
        ClassicAssert.AreEqual(d.Fnan, o.Fnan);
        ClassicAssert.AreEqual(d.Fni, o.Fni);
        ClassicAssert.AreEqual(d.Fpi, o.Fpi);
        ClassicAssert.AreEqual(d.Imax, o.Imax);
        ClassicAssert.AreEqual(d.Imin, o.Imin);
        ClassicAssert.AreEqual(d.Lmax, o.Lmax);
        ClassicAssert.AreEqual(d.Lmin, o.Lmin);
        ClassicAssert.AreEqual(d.Mmax, o.Mmax);
        ClassicAssert.AreEqual(d.Mmin, o.Mmin);
        ClassicAssert.AreEqual(d.UImax, o.UImax);
        ClassicAssert.AreEqual(d.ULmax, o.ULmax);

        // ok
        ClassicAssert.AreEqual(d.Fmax, o.Fmax);
        ClassicAssert.AreEqual(d.Fmin, o.Fmin);
        ClassicAssert.AreEqual(d.MmaxDec, o.MmaxDec);
        ClassicAssert.AreEqual(d.MminDec, o.MminDec);
    }

#pragma warning disable S2094 // An empty type is the case under test
    public class test { }
#pragma warning restore S2094

    [Test]
    public static void ArrayOfObjectExtOff()
    {
        var s = Bjson.ToBjson(new[] { new test(), new test() }, new BjsonParameters { UseExtensions = false });
        var o = Bjson.ToObject<test[]>(s);
        Console.WriteLine(o.GetType().ToString());
        ClassicAssert.AreEqual(typeof(test[]), o.GetType());
    }

    [Test]
    public static void ArrayOfObjectsWithoutTypeInfoToObjectTyped()
    {
        var s = Bjson.ToBjson(new[] { new test(), new test() });
        var o = Bjson.ToObject<test[]>(s);
        Console.WriteLine(o.GetType().ToString());
        ClassicAssert.AreEqual(typeof(test[]), o.GetType());
    }

    [Test]
    public static void ArrayOfObjectsWithTypeInfoToObject()
    {
        var s = Bjson.ToBjson(new[] { new test(), new test() });
        var o = Bjson.ToObject(s);
        Console.WriteLine(o.GetType().ToString());
        var i = o as test[];
        ClassicAssert.AreEqual(typeof(test), i[0].GetType());
    }

    public class KeyAndValue<TKey, TValue>
    {
        public TKey Key { get; set; }
        public TValue Value { get; set; }
    }

    public class Version
    {
        public byte Milestone { get; set; }
        public byte Major { get; set; }
        public byte Minor { get; set; }
        public byte Revision { get; set; }
    }

    public class CommandSendInfo
    {
        public KeyAndValue<string, Version>[] Items { get; set; }
    }

    [Test]
    public static void Longname()
    {
        var input = new CommandSendInfo
        {
            Items = new[]
            {
                new KeyAndValue<string, Version> { Key = "Test", Value = new Version() },
            },
        };

        var bjson = Bjson.ToBjson(input);

        var output = Bjson.ToObject<CommandSendInfo>(bjson);

        ClassicAssert.AreEqual("Test", output.Items[0].Key);
    }

    [Test]
    public static void dicofdic()
    {
        var d = new Dictionary<string, Dictionary<string, string>>();
        var dd = new Dictionary<string, string>();
        dd.Add("Key1", "Value1");
        dd.Add("Key2", "Value2");
        dd.Add("Key3", "Value3");
        dd.Add("Key4", "Value4");
        dd.Add("Key5", "Value5");
        d.Add("Section1", dd);
        var s = Bjson.ToBjson(d, new BjsonParameters { UseExtensions = false });
        var o = Bjson.ToObject<Dictionary<string, Dictionary<string, string>>>(s);
        var v = o["Section1"];

        ClassicAssert.AreEqual(5, v.Count);
        ClassicAssert.AreEqual("Value2", v["Key2"]);
    }

    public enum Letter
    {
        a,
        b,
    }

    [Test]
    public static void RootEnum()
    {
        var e = Letter.b;
        var s = Bjson.ToBjson(e);

        var o = Bjson.ToObject<Letter>(s);
        ClassicAssert.AreEqual(e, o);

        o = Bjson.ToObject<Letter>(s);
        ClassicAssert.AreEqual(e, o);
    }

    private sealed class npc
    {
        public int a = 1;
        public int b = 2;
    }

    [Test]
    public static void NonPublicClass()
    {
        var p = new npc();
        p.a = 10;
        p.b = 20;
        var s = Bjson.ToBjson(p);
        var o = (npc)Bjson.ToObject(s);
        ClassicAssert.AreEqual(10, o.a);
        ClassicAssert.AreEqual(20, o.b);
    }

    public class Item
    {
        public int Id { get; set; }
        public string Data { get; set; }
    }

    public class TestObject
    {
        public int Id { get; set; }
        public string Stuff { get; set; }
        public virtual ObservableCollection<Item> Items { get; set; }
    }

    [Test]
    public static void noncapacitylist()
    {
        TestObject testObject = new TestObject
        {
            Id = 1,
            Stuff = "test",
            Items = new ObservableCollection<Item>(),
        };

        testObject.Items.Add(new Item { Id = 1, Data = "Item 1" });
        testObject.Items.Add(new Item { Id = 2, Data = "Item 2" });

        var s = Bjson.ToBjson(testObject);

        TestObject copyObject = new TestObject();
        Bjson.FillObject(copyObject, s);
        ClassicAssert.AreEqual(2, copyObject.Items.Count);
    }
} // tests.
