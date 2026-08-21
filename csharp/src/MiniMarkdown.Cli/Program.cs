using System;
using System.IO;
using System.Text;

namespace MiniMarkdown.Cli
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            if (args.Length == 0 || HasArgument(args, "--help") || HasArgument(args, "-h"))
            {
                Console.WriteLine("Usage: minimarkdown <input.xlsx|-> [-o output.md]");
                return args.Length == 0 ? 2 : 0;
            }

            string inputPath = args[0];
            string outputPath = ReadOutputPath(args);
            if (outputPath == null && HasArgument(args, "-o"))
            {
                Console.Error.WriteLine("Missing output path after -o.");
                return 2;
            }

            try
            {
                using (Stream input = inputPath == "-" ? Console.OpenStandardInput() : File.OpenRead(inputPath))
                using (TextWriter output = outputPath == null
                    ? new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false), 4096, false)
                    : new StreamWriter(outputPath, false, new UTF8Encoding(false)))
                {
                    new XlsxConverter().Convert(input, output);
                }

                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine("Conversion failed: " + exception.Message);
                return 1;
            }
        }

        private static bool HasArgument(string[] args, string value)
        {
            foreach (string argument in args)
            {
                if (string.Equals(argument, value, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static string ReadOutputPath(string[] args)
        {
            for (int index = 1; index < args.Length; index++)
            {
                if (args[index] == "-o")
                {
                    return index + 1 < args.Length ? args[index + 1] : null;
                }
            }

            return null;
        }
    }
}