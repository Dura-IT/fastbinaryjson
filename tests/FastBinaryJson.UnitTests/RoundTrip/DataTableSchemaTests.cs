using System.Data;
using AwesomeAssertions;
using DuraIT.FastBinaryJson;
using NUnit.Framework;

namespace FastBinaryJson.UnitTests.RoundTrip
{
    /*
     * With UseOptimizedDatasetSchema off, a DataTable carries its schema as XML. Upstream wrote the
     * schema into a StringWriter and then returned the table's NAME instead of the writer's text, so
     * the "schema" in the payload was just "Orders" and the table could not be read back.
     */
    [TestFixture]
    [TestOf(typeof(BJSON))]
    public sealed class DataTableSchemaTests
    {
        [Test]
        public void DataTable_XmlSchemaMode_RoundTripsRows()
        {
            var table = new DataTable("Orders");
            table.Columns.Add("Id", typeof(int));
            table.Columns.Add("Name", typeof(string));
            table.Rows.Add(1, "first");
            table.Rows.Add(2, "second");
            var parameters = new BJSONParameters { UseOptimizedDatasetSchema = false };

            byte[] bytes = BJSON.ToBJSON(table, parameters);
            DataTable? read = BJSON.ToObject<DataTable>(bytes, parameters);

            read.Should().NotBeNull();
            read!.Rows.Count.Should().Be(2);
            read.Rows[1]["Name"].Should().Be("second");
        }
    }
}
