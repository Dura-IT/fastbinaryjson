using System;
using System.Data;
using System.Linq;
using AwesomeAssertions;
using DuraIT.FastBinaryJson;
using NUnit.Framework;

namespace FastBinaryJson.UnitTests.RoundTrip
{
    /*
     * A DataSet or DataTable written with the default optimized schema embeds a DatasetSchema object,
     * and its $type is a library type name. Upstream's is 'fastBinaryJSON.DatasetSchema, fastBinaryJSON,
     * Version=1.5.0.0, ...'. This package has another assembly name, so reading upstream's bytes failed
     * ("Cannot determine type") and writing bytes upstream could not read was the mirror image. The
     * fixtures below were written by upstream fastBinaryJSON 1.6.1 itself.
     */
    [TestFixture]
    [TestOf(typeof(Bjson))]
    public sealed class UpstreamDataSetTests
    {
        private const string UpstreamDataSet =
            "ASAOJABzAGMAaABlAG0AYQAFAR3VAAAAIAokAHQAeQBwAGUABRoCAAAAMQAGIAhJAG4AZgBvAAUDGgIAAABUAAYaAgAAAEEABhoYAAAAUwB5AHMAdABlAG0ALgBJAG4AdAAzADIABhoCAAAAVAAGGgIAAABCAAYaGgAAAFMAeQBzAHQAZQBtAC4AUwB0AHIAaQBuAGcABAYgCE4AYQBtAGUABRoEAAAARABTAAIGIAJUAAUDAwoBAAAABhoCAAAAeAAEBgMKAgAAAAYaAgAAAHkABAQCIAwkAHQAeQBwAGUAcwAFASACMQAFGt4AAABmAGEAcwB0AEIAaQBuAGEAcgB5AEoAUwBPAE4ALgBEAGEAdABhAHMAZQB0AFMAYwBoAGUAbQBhACwAIABmAGEAcwB0AEIAaQBuAGEAcgB5AEoAUwBPAE4ALAAgAFYAZQByAHMAaQBvAG4APQAxAC4ANQAuADAALgAwACwAIABDAHUAbAB0AHUAcgBlAD0AbgBlAHUAdAByAGEAbAAsACAAUAB1AGIAbABpAGMASwBlAHkAVABvAGsAZQBuAD0ANgBiADcANQBhADgAMAA2AGIAOAA2ADAAOQA1AGMAZAAC";
        private const string UpstreamDataTable =
            "ASAOJABzAGMAaABlAG0AYQAFAR2RAAAAIAokAHQAeQBwAGUABRoCAAAAMQAGIAhJAG4AZgBvAAUDGgQAAABUADIABhoCAAAAQQAGGhgAAABTAHkAcwB0AGUAbQAuAEkAbgB0ADMAMgAEBiAITgBhAG0AZQAFGgQAAABUADIAAgYgBFQAMgAFAwMKBQAAAAQEAiAMJAB0AHkAcABlAHMABQEgAjEABRreAAAAZgBhAHMAdABCAGkAbgBhAHIAeQBKAFMATwBOAC4ARABhAHQAYQBzAGUAdABTAGMAaABlAG0AYQAsACAAZgBhAHMAdABCAGkAbgBhAHIAeQBKAFMATwBOACwAIABWAGUAcgBzAGkAbwBuAD0AMQAuADUALgAwAC4AMAAsACAAQwB1AGwAdAB1AHIAZQA9AG4AZQB1AHQAcgBhAGwALAAgAFAAdQBiAGwAaQBjAEsAZQB5AFQAbwBrAGUAbgA9ADYAYgA3ADUAYQA4ADAANgBiADgANgAwADkANQBjAGQAAg==";
        private const string UpstreamSchemaTypeName =
            "fastBinaryJSON.DatasetSchema, fastBinaryJSON, Version=1.5.0.0, Culture=neutral, PublicKeyToken=6b75a806b86095cd";

        [Test]
        public void ToObject_DataSetWrittenByUpstream_ReadsTablesAndRows()
        {
            DataSet? read = Bjson.ToObject<DataSet>(Convert.FromBase64String(UpstreamDataSet));

            read.Should().NotBeNull();
            read.DataSetName.Should().Be("DS");
            read.Tables["T"]!.Rows.Count.Should().Be(2);
            read.Tables["T"]!.Rows[1]["B"].Should().Be("y");
            read.Tables["T"]!.Columns["A"]!.DataType.Should().Be<int>();
        }

        [Test]
        public void ToObject_DataTableWrittenByUpstream_ReadsRows()
        {
            DataTable? read = Bjson.ToObject<DataTable>(Convert.FromBase64String(UpstreamDataTable));

            read.Should().NotBeNull();
            read.TableName.Should().Be("T2");
            read.Rows[0]["A"].Should().Be(5);
        }

        [Test]
        public void ToBjson_DataSet_NamesTheSchemaTypeTheWayUpstreamDoes()
        {
            using var set = new DataSet("DS");
            DataTable table = set.Tables.Add("T");
            table.Columns.Add("A", typeof(int));
            table.Rows.Add(1);

            byte[] bytes = Bjson.ToBjson(set);

            // UTF-16 names sit at either alignment, so drop the zero bytes and compare the ASCII.
            new string(bytes.Where(b => b != 0).Select(b => (char)b).ToArray())
                .Should()
                .Contain(UpstreamSchemaTypeName);
            Bjson.ToObject<DataSet>(bytes)!.Tables["T"]!.Rows[0]["A"].Should().Be(1);
        }
    }
}
