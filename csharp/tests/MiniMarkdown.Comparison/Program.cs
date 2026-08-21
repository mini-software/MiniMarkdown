using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace MiniMarkdown.Comparison
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            if (args.Length != 1 || !File.Exists(args[0]))
            {
                Console.Error.WriteLine("Usage: MiniMarkdown.Comparison <input.xlsx>");
                return 2;
            }

            string directory = Path.Combine(Path.GetTempPath(), "MiniMarkdown-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                string ownOutput = Path.Combine(directory, "minimarkdown.md");
                new XlsxConverter().Convert(args[0], ownOutput);
                DocumentShape baseline = DocumentShape.Read(ownOutput);

                int failures = 0;
                failures += Compare("anydoc", Environment.GetEnvironmentVariable("ANYDOC_COMMAND") ?? "anydoc", args[0], directory, baseline);
                failures += Compare("markitdown", Environment.GetEnvironmentVariable("MARKITDOWN_COMMAND") ?? "markitdown", args[0], directory, baseline);
                return failures == 0 ? 0 : 1;
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        private static int Compare(string name, string command, string input, string directory, DocumentShape baseline)
        {
            string output = Path.Combine(directory, name + ".md");
            ProcessStartInfo start = new ProcessStartInfo
            {
                FileName = command,
                Arguments = Quote(input) + " -o " + Quote(output),
                UseShellExecute = false,
                CreateNoWindow = true
            };

            try
            {
                using (Process process = Process.Start(start))
                {
                    process.WaitForExit();
                    if (process.ExitCode != 0 || !File.Exists(output))
                    {
                        Console.Error.WriteLine(name + " failed with exit code " + process.ExitCode + ".");
                        return 1;
                    }
                }
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(name + " is unavailable: " + exception.Message);
                return 1;
            }

            DocumentShape candidate = DocumentShape.Read(output);
            Console.WriteLine(name + ": headings " + candidate.Headings + " vs " + baseline.Headings +
                ", table rows " + candidate.TableRows + " vs " + baseline.TableRows +
                ", non-empty cells " + candidate.NonEmptyCells + " vs " + baseline.NonEmptyCells);
            return 0;
        }

        private static string Quote(string value)
        {
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }
    }

    internal sealed class DocumentShape
    {
        internal int Headings;
        internal int TableRows;
        internal int NonEmptyCells;

        internal static DocumentShape Read(string path)
        {
            DocumentShape shape = new DocumentShape();
            foreach (string rawLine in File.ReadLines(path))
            {
                string line = rawLine.Trim();
                if (line.StartsWith("#", StringComparison.Ordinal))
                {
                    shape.Headings++;
                }

                if (!line.StartsWith("|", StringComparison.Ordinal) || IsSeparator(line))
                {
                    continue;
                }

                shape.TableRows++;
                string[] cells = line.Trim('|').Split('|');
                foreach (string cell in cells)
                {
                    if (cell.Trim().Length != 0)
                    {
                        shape.NonEmptyCells++;
                    }
                }
            }

            return shape;
        }

        private static bool IsSeparator(string line)
        {
            string compact = line.Replace("|", string.Empty).Replace("-", string.Empty).Replace(":", string.Empty).Replace(" ", string.Empty);
            return compact.Length == 0 && line.IndexOf('-') >= 0;
        }
    }
}