using System;
using System.Collections.Generic;
using System.Globalization;

namespace FastBinaryJson.Benchmarks
{
    /// <summary>
    /// Deterministic (non-timed) reports: which combinations round-trip, and how large the
    /// encoded payloads are raw, gzipped and brotli'd. Size is a property of the format, not a
    /// measurement, so it is computed once rather than benchmarked.
    /// </summary>
    public static class Reports
    {
        public static void PrintCompatibilityMatrix(IReadOnlyList<PayloadCase> payloads, IReadOnlyList<ISerializerArm> arms)
        {
            Console.WriteLine("## Round-trip compatibility");
            Console.WriteLine();
            Console.WriteLine("Serialize, deserialize, re-serialize, compare bytes. A failure here means the");
            Console.WriteLine("combination cannot be honestly benchmarked, not that it is slow.");
            Console.WriteLine();

            foreach (PayloadCase payload in payloads)
            {
                Console.WriteLine(string.Concat("### ", payload.Name, "  (", payload.Shape, ")"));

                foreach (ISerializerArm arm in arms)
                {
                    bool ok = payload.TryRoundTrip(arm, out string error);
                    string status = ok ? "PASS" : "FAIL";
                    string detail = ok ? string.Empty : string.Concat("  -  ", error);
                    Console.WriteLine(string.Concat("  ", status, "  ", arm.Name.PadRight(24), detail));
                }

                Console.WriteLine();
            }
        }

        public static void PrintSizeTable(IReadOnlyList<PayloadCase> payloads, IReadOnlyList<ISerializerArm> arms)
        {
            Console.WriteLine("## Encoded size (bytes)");
            Console.WriteLine();

            foreach (PayloadCase payload in payloads)
            {
                Console.WriteLine(string.Concat("### ", payload.Name, "  (", payload.Shape, ")"));
                Console.WriteLine();
                Console.WriteLine("| Serializer | Raw | Gzip | Brotli | Raw vs STJ |");
                Console.WriteLine("|---|---:|---:|---:|---:|");

                int textBaseline = 0;
                List<string> rows = new List<string>();

                foreach (ISerializerArm arm in arms)
                {
                    if (!payload.TryRoundTrip(arm, out string _))
                    {
                        rows.Add(string.Concat("| ", arm.Name, " | n/a | n/a | n/a | round-trip failed |"));
                        continue;
                    }

                    byte[] encoded = payload.Serialize(arm);
                    int gzip = Compression.GzipSize(encoded);
                    int brotli = Compression.BrotliSize(encoded);

                    if (arm is SystemTextJsonArm)
                    {
                        textBaseline = encoded.Length;
                    }

                    rows.Add(string.Concat(
                        "| ", arm.Name,
                        " | ", encoded.Length.ToString("N0", CultureInfo.InvariantCulture),
                        " | ", gzip.ToString("N0", CultureInfo.InvariantCulture),
                        " | ", brotli.ToString("N0", CultureInfo.InvariantCulture),
                        " | {RATIO:", encoded.Length.ToString(), "} |"));
                }

                foreach (string row in rows)
                {
                    Console.WriteLine(Resolve(row, textBaseline));
                }

                Console.WriteLine();
            }
        }

        private static string Resolve(string row, int textBaseline)
        {
            int start = row.IndexOf("{RATIO:", StringComparison.Ordinal);
            if (start < 0)
            {
                return row;
            }

            int end = row.IndexOf('}', start);
            string raw = row.Substring(start + 7, end - start - 7);
            string replacement = "n/a";

            if (textBaseline > 0 && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int size))
            {
                double ratio = (double)size / textBaseline;
                replacement = string.Concat(ratio.ToString("0.00", CultureInfo.InvariantCulture), "x");
            }

            return string.Concat(row.Substring(0, start), replacement, row.Substring(end + 1));
        }
    }
}
