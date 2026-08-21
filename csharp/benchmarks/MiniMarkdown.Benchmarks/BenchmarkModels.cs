using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Text;

namespace MiniMarkdown.Benchmarks
{
    internal sealed class BenchmarkOptions
    {
        internal string CorpusDirectory;
        internal string ReportDirectory;
        internal string Filter;
        internal string ToolNames;
        internal int Iterations;
        internal int Warmups;
        internal bool SkipGenerate;
        internal string AnydocCommand;
        internal string MarkItDownCommand;
    }

    internal sealed class ToolDefinition
    {
        internal string Name;
        internal string Command;
        internal string FixedArguments;
        internal bool IsMiniMarkdown;
    }

    [DataContract]
    internal sealed class BenchmarkReport
    {
        [DataMember(Order = 1)] internal string GeneratedAtUtc;
        [DataMember(Order = 2)] internal int Iterations;
        [DataMember(Order = 3)] internal int Warmups;
        [DataMember(Order = 4)] internal List<BenchmarkCaseResult> Cases = new List<BenchmarkCaseResult>();
    }

    [DataContract]
    internal sealed class BenchmarkCaseResult
    {
        [DataMember(Order = 1)] internal string Name;
        [DataMember(Order = 2)] internal string Description;
        [DataMember(Order = 3)] internal long InputBytes;
        [DataMember(Order = 4)] internal List<ToolResult> Tools = new List<ToolResult>();
    }

    [DataContract]
    internal sealed class ToolResult
    {
        [DataMember(Order = 1)] internal string Name;
        [DataMember(Order = 2)] internal bool Available;
        [DataMember(Order = 3, EmitDefaultValue = false)] internal string Error;
        [DataMember(Order = 4)] internal double MedianMilliseconds;
        [DataMember(Order = 5)] internal double MinimumMilliseconds;
        [DataMember(Order = 6)] internal long PeakWorkingSetBytes;
        [DataMember(Order = 7)] internal long OutputBytes;
        [DataMember(Order = 8)] internal double SemanticShapeScore;
        [DataMember(Order = 9, EmitDefaultValue = false)] internal DocumentShape Shape;
    }

    [DataContract]
    internal sealed class DocumentShape
    {
        [DataMember(Order = 1)] internal int Headings;
        [DataMember(Order = 2)] internal int TableRows;
        [DataMember(Order = 3)] internal int MaximumColumns;
        [DataMember(Order = 4)] internal long NonEmptyCells;

        internal static DocumentShape Read(string path)
        {
            DocumentShape shape = new DocumentShape();
            foreach (string rawLine in FileLineReader.ReadLines(path))
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

                IList<string> cells = SplitCells(line);
                shape.TableRows++;
                shape.MaximumColumns = Math.Max(shape.MaximumColumns, cells.Count);
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

        internal static double Similarity(DocumentShape baseline, DocumentShape candidate)
        {
            return (Ratio(baseline.Headings, candidate.Headings) +
                    Ratio(baseline.TableRows, candidate.TableRows) +
                    Ratio(baseline.MaximumColumns, candidate.MaximumColumns) +
                    Ratio(baseline.NonEmptyCells, candidate.NonEmptyCells)) / 4.0;
        }

        private static double Ratio(long left, long right)
        {
            if (left == 0 && right == 0) return 1;
            if (left == 0 || right == 0) return 0;
            return (double)Math.Min(left, right) / Math.Max(left, right);
        }

        private static bool IsSeparator(string line)
        {
            string compact = line.Replace("|", string.Empty).Replace("-", string.Empty).Replace(":", string.Empty).Replace(" ", string.Empty);
            return compact.Length == 0 && line.IndexOf('-') >= 0;
        }

        private static IList<string> SplitCells(string line)
        {
            List<string> cells = new List<string>();
            StringBuilder value = new StringBuilder();
            bool escaped = false;
            for (int index = 1; index < line.Length - 1; index++)
            {
                char character = line[index];
                if (escaped)
                {
                    value.Append(character);
                    escaped = false;
                }
                else if (character == '\\')
                {
                    escaped = true;
                    value.Append(character);
                }
                else if (character == '|')
                {
                    cells.Add(value.ToString());
                    value.Length = 0;
                }
                else
                {
                    value.Append(character);
                }
            }
            cells.Add(value.ToString());
            return cells;
        }
    }

    internal static class FileLineReader
    {
        internal static IEnumerable<string> ReadLines(string path)
        {
            using (System.IO.StreamReader reader = System.IO.File.OpenText(path))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    yield return line;
                }
            }
        }
    }
}