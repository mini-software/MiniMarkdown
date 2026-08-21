using System;
using System.Globalization;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Xml;

namespace MiniMarkdown.Benchmarks
{
    internal static class BenchmarkReportWriter
    {
        internal static void Write(BenchmarkReport report, string directory)
        {
            string jsonPath = Path.Combine(directory, "benchmark_report.json");
            using (FileStream stream = File.Create(jsonPath))
            using (XmlDictionaryWriter writer = JsonReaderWriterFactory.CreateJsonWriter(stream, Encoding.UTF8, true, true))
            {
                new DataContractJsonSerializer(typeof(BenchmarkReport)).WriteObject(writer, report);
            }

            using (StreamWriter writer = new StreamWriter(Path.Combine(directory, "benchmark_report.md"), false, new UTF8Encoding(false)))
            {
                writer.WriteLine("# MiniMarkdown XLSX Benchmark");
                writer.WriteLine();
                writer.WriteLine("Generated: " + report.GeneratedAtUtc);
                writer.WriteLine();
                writer.WriteLine("Measured iterations: " + report.Iterations + "; warmups: " + report.Warmups + ". Each conversion runs in an isolated process.");
                writer.WriteLine();
                writer.WriteLine("| Case | Tool | Median ms | Min ms | Peak MiB | Output KiB | Shape score |");
                writer.WriteLine("| --- | --- | ---: | ---: | ---: | ---: | ---: |");
                foreach (BenchmarkCaseResult benchmarkCase in report.Cases)
                {
                    foreach (ToolResult tool in benchmarkCase.Tools)
                    {
                        writer.Write("| ");
                        writer.Write(Escape(benchmarkCase.Name));
                        writer.Write(" | ");
                        writer.Write(Escape(tool.Name));
                        if (!tool.Available)
                        {
                            writer.Write(" | unavailable |  |  |  |  |");
                        }
                        else
                        {
                            writer.Write(" | ");
                            writer.Write(tool.MedianMilliseconds.ToString("0.00", CultureInfo.InvariantCulture));
                            writer.Write(" | ");
                            writer.Write(tool.MinimumMilliseconds.ToString("0.00", CultureInfo.InvariantCulture));
                            writer.Write(" | ");
                            writer.Write((tool.PeakWorkingSetBytes / 1048576.0).ToString("0.00", CultureInfo.InvariantCulture));
                            writer.Write(" | ");
                            writer.Write((tool.OutputBytes / 1024.0).ToString("0.00", CultureInfo.InvariantCulture));
                            writer.Write(" | ");
                            writer.Write(tool.SemanticShapeScore.ToString("0.000", CultureInfo.InvariantCulture));
                            writer.Write(" |");
                        }
                        writer.WriteLine();
                    }
                }

                writer.WriteLine();
                writer.WriteLine("## Case Details");
                foreach (BenchmarkCaseResult benchmarkCase in report.Cases)
                {
                    writer.WriteLine();
                    writer.WriteLine("### " + benchmarkCase.Name);
                    writer.WriteLine();
                    writer.WriteLine(benchmarkCase.Description);
                    writer.WriteLine();
                    writer.WriteLine("Input size: " + benchmarkCase.InputBytes.ToString("N0", CultureInfo.InvariantCulture) + " bytes.");
                    foreach (ToolResult tool in benchmarkCase.Tools)
                    {
                        if (!tool.Available)
                        {
                            writer.WriteLine("- " + tool.Name + ": unavailable (" + tool.Error + ")");
                        }
                        else
                        {
                            writer.WriteLine("- " + tool.Name + ": " + tool.Shape.Headings + " headings, " + tool.Shape.TableRows + " table rows, " + tool.Shape.MaximumColumns + " max columns, " + tool.Shape.NonEmptyCells + " non-empty cells.");
                        }
                    }
                }

                writer.WriteLine();
                writer.WriteLine("Shape score compares headings, table rows, maximum columns, and non-empty cells against MiniMarkdown. It is a semantic diagnostic, not a byte-for-byte correctness score.");
            }
        }

        private static string Escape(string value)
        {
            return (value ?? string.Empty).Replace("\\", "\\\\").Replace("|", "\\|");
        }
    }
}