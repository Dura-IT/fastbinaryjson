using System;
using System.Collections.Generic;
using FastBinaryJson.Benchmarks.Corpus;

namespace FastBinaryJson.Benchmarks
{
    /// <summary>
    /// Targeted fidelity checks for specific suspected defects.
    ///
    /// These exist because the compatibility matrix cannot catch every class of bug. That check
    /// is serialize, deserialize, re-serialize, compare bytes - which proves the encoding is
    /// STABLE, not that it is CORRECT. A defect that corrupts symmetrically, so that the second
    /// encoding reproduces the first, passes it while still having destroyed data. These probes
    /// compare against the original values instead.
    /// </summary>
    public static class DefectProbes
    {
        public static void Run()
        {
            Console.WriteLine("## Defect probes");
            Console.WriteLine();
            Console.WriteLine("Compares round-tripped values against the ORIGINALS, not against a re-encoding.");
            Console.WriteLine();

            ProbeChar();
            ProbeLongDictionaryKey();
        }

        private static void ProbeChar()
        {
            Console.WriteLine("### char round-trip");

            CharHolder original = PayloadFactory.CreateCharHolder();
            ISerializerArm arm = Payloads.ArmByKey("fbj-utf16");

            try
            {
                byte[] encoded = arm.Serialize(original);
                CharHolder restored = arm.Deserialize<CharHolder>(encoded);
                Report("typed deserialize", restored.Value == original.Value, string.Concat("expected '", original.Value.ToString(), "', got '", restored.Value.ToString(), "'"));
            }
            catch (Exception ex)
            {
                Report("typed deserialize", false, string.Concat("threw ", ex.GetType().Name, ": ", ex.Message));
            }

            try
            {
                byte[] encoded = arm.Serialize(original);
                object untyped = global::fastBinaryJSON.BJSON.Parse(encoded);
                Dictionary<string, object> asMap = (Dictionary<string, object>)untyped;
                object value = asMap["Value"];
                Report("untyped BJSON.Parse", value is char, string.Concat("expected a char, got ", value.GetType().Name, " with value ", value.ToString()));
            }
            catch (Exception ex)
            {
                Report("untyped BJSON.Parse", false, string.Concat("threw ", ex.GetType().Name, ": ", ex.Message));
            }

            Console.WriteLine();
        }

        private static void ProbeLongDictionaryKey()
        {
            Console.WriteLine("### dictionary key length (WriteName writes the length into a single byte)");

            Dictionary<string, string> original = PayloadFactory.CreateLongKeyDictionary();

            foreach (string armKey in new string[] { "fbj-utf16", "fbj-utf8" })
            {
                ISerializerArm arm = Payloads.ArmByKey(armKey);

                foreach (KeyValuePair<string, string> entry in original)
                {
                    if (entry.Key.Length < 100)
                    {
                        continue;
                    }

                    try
                    {
                        byte[] encoded = arm.Serialize(original);
                        Dictionary<string, string> restored = arm.Deserialize<Dictionary<string, string>>(encoded);
                        bool present = restored.ContainsKey(entry.Key);
                        string detail = present
                            ? string.Concat("key of ", entry.Key.Length.ToString(), " chars survived")
                            : string.Concat("key of ", entry.Key.Length.ToString(), " chars LOST; restored keys: ", DescribeKeys(restored));
                        Report(string.Concat(arm.Name, " long key"), present, detail);
                    }
                    catch (Exception ex)
                    {
                        Report(string.Concat(arm.Name, " long key"), false, string.Concat("threw ", ex.GetType().Name, ": ", ex.Message));
                    }
                }
            }

            Console.WriteLine();
        }

        private static string DescribeKeys(Dictionary<string, string> map)
        {
            List<string> described = new List<string>(map.Count);
            foreach (KeyValuePair<string, string> entry in map)
            {
                described.Add(string.Concat("\"", Truncate(entry.Key, 30), "\" (", entry.Key.Length.ToString(), " chars)"));
            }

            return string.Join(", ", described);
        }

        private static string Truncate(string value, int max) => value.Length <= max ? value : string.Concat(value.Substring(0, max), "...");

        private static void Report(string what, bool ok, string detail)
        {
            Console.WriteLine(string.Concat("  ", ok ? "PASS" : "FAIL", "  ", what.PadRight(34), "  ", detail));
        }
    }
}
