using System;
using System.Collections.Generic;
using System.Globalization;

namespace FastBinaryJson.Benchmarks
{
    /// <summary>
    /// Deterministic (non-timed) reports: which combinations round-trip, and how large the encoded
    /// payloads are raw, gzipped and brotli'd.
    ///
    /// Size is a property of the format, so it is computed once rather than benchmarked. The
    /// compression calls are deliberately kept off every timed path: a fresh GZipStream plus its
    /// MemoryStreams costs far more per blob than deflate spends on the bytes, so timing them
    /// would measure stream plumbing and report it as codec cost.
    /// </summary>
    internal static class ReportWriter
    {
        private sealed record SizeRow(string Name, bool Supported, int Raw, int Gzip, int Brotli);

        public static void PrintCompatibilityMatrix(IReadOnlyList<IPayloadCase> payloads, IReadOnlyList<ISerializerArm> arms)
        {
            ArgumentNullException.ThrowIfNull(payloads);
            ArgumentNullException.ThrowIfNull(arms);
            Console.WriteLine("## Round-trip compatibility");
            Console.WriteLine();
            Console.WriteLine("Serialize, deserialize, re-serialize, compare bytes. A failure here means the");
            Console.WriteLine("combination cannot be honestly benchmarked, not that it is slow.");
            Console.WriteLine();

            foreach (IPayloadCase payload in payloads)
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

        /// <summary>
        /// Whether the fork and upstream 1.6.1 write the same bytes for each payload. Identical
        /// output means both arms do the same work, so any timing difference is implementation.
        /// </summary>
        public static void PrintUpstreamIdentity(IReadOnlyList<IPayloadCase> payloads)
        {
            ArgumentNullException.ThrowIfNull(payloads);
            Console.WriteLine("## Fork vs upstream 1.6.1 output");
            Console.WriteLine();

            foreach (IPayloadCase payload in payloads)
            {
                foreach (bool unicode in new[] { true, false })
                {
                    string verdict = CompareOutput(payload, new FastBinaryJsonArm(unicode), new UpstreamFastBinaryJsonArm(unicode));
                    Console.WriteLine(string.Concat("  ", payload.Name.PadRight(18), unicode ? "UTF-16  " : "UTF-8   ", verdict));
                }
            }

            Console.WriteLine();
        }

        private static string CompareOutput(IPayloadCase payload, ISerializerArm fork, ISerializerArm upstream)
        {
            try
            {
                byte[] ours = payload.Serialize(fork);
                byte[] theirs = payload.Serialize(upstream);
                int length = Math.Min(ours.Length, theirs.Length);
                for (int i = 0; i < length; i++)
                {
                    if (ours[i] != theirs[i])
                    {
                        return string.Concat("DIFFERS at byte ", i.ToString(CultureInfo.InvariantCulture));
                    }
                }

                if (ours.Length != theirs.Length)
                {
                    return string.Concat(
                        "DIFFERS in length: ",
                        ours.Length.ToString(CultureInfo.InvariantCulture),
                        " vs ",
                        theirs.Length.ToString(CultureInfo.InvariantCulture)
                    );
                }

                return "IDENTICAL";
            }
#pragma warning disable CA1031 // A comparison that throws is reported as a FAIL row, not propagated.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                return string.Concat("FAIL  -  ", ex.GetType().Name, ": ", ex.Message);
            }
        }

        public static void PrintSizeTable(IReadOnlyList<IPayloadCase> payloads, IReadOnlyList<ISerializerArm> arms)
        {
            ArgumentNullException.ThrowIfNull(payloads);
            ArgumentNullException.ThrowIfNull(arms);
            Console.WriteLine("## Encoded size (bytes)");
            Console.WriteLine();
            Console.WriteLine("`Gzip shrink` is gzip relative to that serializer's own raw output - how much slack");
            Console.WriteLine("the encoding left behind. `vs STJ` columns compare against System.Text.Json at the");
            Console.WriteLine("same compression level, so the raw and gzipped verdicts can be read independently:");
            Console.WriteLine("a format can win on raw bytes and lose once both sides are compressed.");
            Console.WriteLine();

            foreach (IPayloadCase payload in payloads)
            {
                List<SizeRow> rows = BuildRows(payload, arms);
                SizeRow? baseline = FindBaseline(rows, arms);

                Console.WriteLine(string.Concat("### ", payload.Name, "  (", payload.Shape, ")"));
                Console.WriteLine();
                Console.WriteLine("| Serializer | Raw | Gzip | Brotli | Gzip shrink | Raw vs STJ | Gzip vs STJ |");
                Console.WriteLine("|---|---:|---:|---:|---:|---:|---:|");

                foreach (SizeRow row in rows)
                {
                    if (!row.Supported)
                    {
                        Console.WriteLine(string.Concat("| ", row.Name, " | n/a | n/a | n/a | n/a | n/a | round-trip failed |"));
                        continue;
                    }

                    Console.WriteLine(
                        string.Concat(
                            "| ",
                            row.Name,
                            " | ",
                            Number(row.Raw),
                            " | ",
                            Number(row.Gzip),
                            " | ",
                            Number(row.Brotli),
                            " | ",
                            Percent(row.Gzip, row.Raw),
                            " | ",
                            Ratio(row.Raw, baseline?.Raw),
                            " | ",
                            Ratio(row.Gzip, baseline?.Gzip),
                            " |"
                        )
                    );
                }

                Console.WriteLine();
            }
        }

        private static List<SizeRow> BuildRows(IPayloadCase payload, IReadOnlyList<ISerializerArm> arms)
        {
            List<SizeRow> rows = new List<SizeRow>(arms.Count);

            foreach (ISerializerArm arm in arms)
            {
                if (!payload.TryRoundTrip(arm, out string _))
                {
                    rows.Add(new SizeRow(arm.Name, false, 0, 0, 0));
                    continue;
                }

                byte[] encoded = payload.Serialize(arm);
                rows.Add(new SizeRow(arm.Name, true, encoded.Length, PayloadCompression.GzipSize(encoded), PayloadCompression.BrotliSize(encoded)));
            }

            return rows;
        }

        private static SizeRow? FindBaseline(List<SizeRow> rows, IReadOnlyList<ISerializerArm> arms)
        {
            for (int i = 0; i < arms.Count; i++)
            {
                if (arms[i] is SystemTextJsonArm && rows[i].Supported)
                {
                    return rows[i];
                }
            }

            return null;
        }

        private static string Number(int value) => value.ToString("N0", CultureInfo.InvariantCulture);

        private static string Ratio(int value, int? baseline)
        {
            if (baseline is null || baseline.Value <= 0)
            {
                return "n/a";
            }

            double ratio = (double)value / baseline.Value;
            return string.Concat(ratio.ToString("0.00", CultureInfo.InvariantCulture), "x");
        }

        private static string Percent(int compressed, int raw)
        {
            if (raw <= 0)
            {
                return "n/a";
            }

            // Negative when compression grew the payload, which happens on inputs small enough
            // that the gzip header outweighs anything deflate can save.
            double saved = 100.0 * (1.0 - ((double)compressed / raw));
            string sign = saved < 0.0 ? "+" : "-";
            return string.Concat(sign, Math.Abs(saved).ToString("0.0", CultureInfo.InvariantCulture), "%");
        }
    }
}
