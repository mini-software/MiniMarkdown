using System;
using System.IO;

namespace MiniMarkdown.Benchmarks
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            if (args.Length > 0 && string.Equals(args[0], "generate", StringComparison.OrdinalIgnoreCase))
            {
                string output = ReadOption(args, "--output") ?? Path.Combine("csharp", "benchmarks", "artifacts", "corpus");
                string filter = ReadOption(args, "--filter");
                int count = XlsxCorpusGenerator.Generate(output, filter);
                Console.WriteLine("Generated " + count + " XLSX benchmark case(s) in " + Path.GetFullPath(output));
                return 0;
            }

            if (args.Length > 0 && string.Equals(args[0], "worker", StringComparison.OrdinalIgnoreCase))
            {
                if (args.Length != 3)
                {
                    Console.Error.WriteLine("Usage: MiniMarkdown.Benchmarks worker <input.xlsx> <output.md>");
                    return 2;
                }

                new XlsxConverter().Convert(args[1], args[2]);
                return 0;
            }

            if (args.Length > 0 && string.Equals(args[0], "run", StringComparison.OrdinalIgnoreCase))
            {
                BenchmarkOptions options = new BenchmarkOptions
                {
                    CorpusDirectory = ReadOption(args, "--corpus") ?? Path.Combine("csharp", "benchmarks", "artifacts", "corpus"),
                    ReportDirectory = ReadOption(args, "--reports") ?? Path.Combine("csharp", "benchmarks", "artifacts", "reports"),
                    Filter = ReadOption(args, "--filter"),
                    ToolNames = ReadOption(args, "--tools") ?? "minimarkdown,anydoc,markitdown",
                    Iterations = ReadIntOption(args, "--iterations", 3),
                    Warmups = ReadIntOption(args, "--warmups", 1),
                    SkipGenerate = HasOption(args, "--skip-generate"),
                    AnydocCommand = ReadOption(args, "--anydoc") ?? Environment.GetEnvironmentVariable("ANYDOC_COMMAND") ?? "anydoc",
                    MarkItDownCommand = ReadOption(args, "--markitdown") ?? Environment.GetEnvironmentVariable("MARKITDOWN_COMMAND") ?? "markitdown"
                };
                return BenchmarkRunner.Run(options);
            }

            Console.WriteLine("Usage: MiniMarkdown.Benchmarks <generate|run|worker> [options]");
            return 2;
        }

        private static string ReadOption(string[] args, string name)
        {
            for (int index = 1; index + 1 < args.Length; index++)
            {
                if (string.Equals(args[index], name, StringComparison.OrdinalIgnoreCase))
                {
                    return args[index + 1];
                }
            }

            return null;
        }

        private static int ReadIntOption(string[] args, string name, int defaultValue)
        {
            string value = ReadOption(args, name);
            int parsed;
            if (value == null)
            {
                return defaultValue;
            }

            if (!int.TryParse(value, out parsed) || parsed < 0)
            {
                throw new ArgumentException(name + " must be a non-negative integer.");
            }

            return parsed;
        }

        private static bool HasOption(string[] args, string name)
        {
            foreach (string argument in args)
            {
                if (string.Equals(argument, name, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}