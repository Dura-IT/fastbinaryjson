using System;
using System.Collections.Generic;
using BenchmarkDotNet.Running;

namespace FastBinaryJson.Benchmarks
{
    public static class Program
    {
        public static int Main(string[] args)
        {
            string mode = args.Length > 0 ? args[0].ToLowerInvariant() : "bench";

            IReadOnlyList<PayloadCase> payloads = Payloads.All();
            IReadOnlyList<ISerializerArm> arms = Payloads.AllArms();

            switch (mode)
            {
                case "validate":
                    Reports.PrintCompatibilityMatrix(payloads, arms);
                    return 0;

                case "sizes":
                    Reports.PrintCompatibilityMatrix(payloads, arms);
                    Reports.PrintSizeTable(payloads, arms);
                    return 0;

                case "bench":
                    BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(SkipFirst(args));
                    return 0;

                default:
                    Console.Error.WriteLine("usage: FastBinaryJson.Benchmarks [validate|sizes|bench]");
                    return 1;
            }
        }

        private static string[] SkipFirst(string[] args)
        {
            if (args.Length <= 1)
            {
                return Array.Empty<string>();
            }

            string[] rest = new string[args.Length - 1];
            Array.Copy(args, 1, rest, 0, rest.Length);
            return rest;
        }
    }
}
