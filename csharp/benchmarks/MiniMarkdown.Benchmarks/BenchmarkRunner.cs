using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace MiniMarkdown.Benchmarks
{
    internal static class BenchmarkRunner
    {
        internal static int Run(BenchmarkOptions options)
        {
            if (options.Iterations < 1)
            {
                Console.Error.WriteLine("At least one measured iteration is required.");
                return 2;
            }

            if (!options.SkipGenerate)
            {
                XlsxCorpusGenerator.Generate(options.CorpusDirectory, options.Filter);
            }

            Directory.CreateDirectory(options.ReportDirectory);
            string workDirectory = Path.Combine(options.ReportDirectory, "work");
            RecreateDirectory(workDirectory);
            IList<ToolDefinition> tools = CreateTools(options);
            Dictionary<string, string> descriptions = XlsxCorpusGenerator.CreateCases().ToDictionary(item => item.Name, item => item.Description, StringComparer.OrdinalIgnoreCase);
            BenchmarkReport report = new BenchmarkReport
            {
                GeneratedAtUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                Iterations = options.Iterations,
                Warmups = options.Warmups
            };

            foreach (string input in Directory.GetFiles(options.CorpusDirectory, "*.xlsx").OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                string name = Path.GetFileNameWithoutExtension(input);
                if (!Matches(name, options.Filter))
                {
                    continue;
                }

                Console.WriteLine();
                Console.WriteLine("[Case] " + name + " (" + new FileInfo(input).Length.ToString("N0", CultureInfo.InvariantCulture) + " bytes)");
                string description;
                descriptions.TryGetValue(name, out description);
                BenchmarkCaseResult caseResult = new BenchmarkCaseResult { Name = name, Description = description ?? string.Empty, InputBytes = new FileInfo(input).Length };
                foreach (ToolDefinition tool in tools)
                {
                    ToolResult result = Measure(tool, input, workDirectory, options.Warmups, options.Iterations);
                    caseResult.Tools.Add(result);
                    PrintResult(result);
                }

                ToolResult baseline = caseResult.Tools.FirstOrDefault(item => item.Name == "MiniMarkdown" && item.Available);
                if (baseline != null)
                {
                    foreach (ToolResult result in caseResult.Tools.Where(item => item.Available))
                    {
                        result.SemanticShapeScore = DocumentShape.Similarity(baseline.Shape, result.Shape);
                    }
                }
                report.Cases.Add(caseResult);
            }

            BenchmarkReportWriter.Write(report, options.ReportDirectory);
            Directory.Delete(workDirectory, true);
            Console.WriteLine();
            Console.WriteLine("Report: " + Path.GetFullPath(Path.Combine(options.ReportDirectory, "benchmark_report.md")));
            return report.Cases.Count == 0 ? 1 : 0;
        }

        private static ToolResult Measure(ToolDefinition tool, string input, string workDirectory, int warmups, int iterations)
        {
            ToolResult result = new ToolResult { Name = tool.Name };
            List<double> elapsed = new List<double>();
            long peakWorkingSet = 0;
            string lastOutput = null;
            try
            {
                for (int index = 0; index < warmups + iterations; index++)
                {
                    string output = Path.Combine(workDirectory, Path.GetFileNameWithoutExtension(input) + "_" + tool.Name + "_" + index + ".md");
                    ProcessMeasurement measurement = RunProcess(tool, input, output);
                    if (measurement.ExitCode != 0 || !File.Exists(output))
                    {
                        string detail = string.IsNullOrWhiteSpace(measurement.StandardError) ? string.Empty : " " + measurement.StandardError.Trim();
                        throw new InvalidOperationException(tool.Name + " exited with code " + measurement.ExitCode + "." + detail);
                    }

                    if (index >= warmups)
                    {
                        elapsed.Add(measurement.ElapsedMilliseconds);
                        peakWorkingSet = Math.Max(peakWorkingSet, measurement.PeakWorkingSetBytes);
                        lastOutput = output;
                    }
                    else
                    {
                        File.Delete(output);
                    }
                }

                elapsed.Sort();
                result.Available = true;
                result.MinimumMilliseconds = elapsed[0];
                result.MedianMilliseconds = elapsed.Count % 2 == 0
                    ? (elapsed[elapsed.Count / 2 - 1] + elapsed[elapsed.Count / 2]) / 2.0
                    : elapsed[elapsed.Count / 2];
                result.PeakWorkingSetBytes = peakWorkingSet;
                result.OutputBytes = new FileInfo(lastOutput).Length;
                result.Shape = DocumentShape.Read(lastOutput);
            }
            catch (Exception exception)
            {
                result.Available = false;
                result.Error = exception.Message;
            }
            return result;
        }

        private static ProcessMeasurement RunProcess(ToolDefinition tool, string input, string output)
        {
            string conversionArguments = tool.IsMiniMarkdown
                ? "worker " + Quote(input) + " " + Quote(output)
                : Quote(input) + " -o " + Quote(output);
            string arguments = string.IsNullOrEmpty(tool.FixedArguments) ? conversionArguments : tool.FixedArguments + " " + conversionArguments;
            string command = tool.Command;
            if (command.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) || command.EndsWith(".bat", StringComparison.OrdinalIgnoreCase))
            {
                arguments = "/d /s /c \"\"" + command + "\" " + arguments + "\"";
                command = Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe";
            }

            ProcessStartInfo start = new ProcessStartInfo
            {
                FileName = command,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                WorkingDirectory = Environment.CurrentDirectory
            };
            Stopwatch stopwatch = Stopwatch.StartNew();
            using (Process process = Process.Start(start))
            {
                StringBuilder standardError = new StringBuilder();
                process.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs eventArgs)
                {
                    if (eventArgs.Data != null)
                    {
                        lock (standardError)
                        {
                            if (standardError.Length < 8192)
                            {
                                standardError.AppendLine(eventArgs.Data);
                            }
                        }
                    }
                };
                process.BeginErrorReadLine();
                long peakWorkingSet = 0;
                do
                {
                    try
                    {
                        process.Refresh();
                        peakWorkingSet = Math.Max(peakWorkingSet, process.WorkingSet64);
                        if (!tool.IsMiniMarkdown)
                        {
                            peakWorkingSet = Math.Max(peakWorkingSet, ProcessTreeMemory.GetWorkingSetBytes(process.Id));
                        }
                    }
                    catch (InvalidOperationException)
                    {
                    }
                }
                while (!process.WaitForExit(10));
                process.WaitForExit();
                stopwatch.Stop();
                return new ProcessMeasurement
                {
                    ExitCode = process.ExitCode,
                    ElapsedMilliseconds = stopwatch.Elapsed.TotalMilliseconds,
                    PeakWorkingSetBytes = peakWorkingSet,
                    StandardError = standardError.ToString()
                };
            }
        }

        private static IList<ToolDefinition> CreateTools(BenchmarkOptions options)
        {
            HashSet<string> requested = new HashSet<string>(options.ToolNames.Split(','), StringComparer.OrdinalIgnoreCase);
            List<ToolDefinition> tools = new List<ToolDefinition>();
            if (requested.Contains("minimarkdown")) tools.Add(new ToolDefinition { Name = "MiniMarkdown", Command = Process.GetCurrentProcess().MainModule.FileName, IsMiniMarkdown = true });
            if (requested.Contains("anydoc")) tools.Add(ParseTool("anydoc", options.AnydocCommand));
            if (requested.Contains("markitdown")) tools.Add(ParseTool("MarkItDown", options.MarkItDownCommand));
            return tools;
        }

        private static ToolDefinition ParseTool(string name, string commandLine)
        {
            commandLine = (commandLine ?? string.Empty).Trim();
            if (commandLine.Length == 0)
            {
                return new ToolDefinition { Name = name, Command = name };
            }

            string command;
            string arguments;
            if (commandLine[0] == '"')
            {
                int closingQuote = commandLine.IndexOf('"', 1);
                if (closingQuote < 0)
                {
                    throw new ArgumentException("The command line for " + name + " has an unmatched quote.");
                }

                command = commandLine.Substring(1, closingQuote - 1);
                arguments = commandLine.Substring(closingQuote + 1).Trim();
            }
            else
            {
                int space = commandLine.IndexOf(' ');
                command = space < 0 ? commandLine : commandLine.Substring(0, space);
                arguments = space < 0 ? string.Empty : commandLine.Substring(space + 1).Trim();
            }

            return new ToolDefinition { Name = name, Command = command, FixedArguments = arguments };
        }

        private static void PrintResult(ToolResult result)
        {
            if (!result.Available)
            {
                Console.WriteLine("  " + result.Name.PadRight(14) + " unavailable: " + result.Error);
                return;
            }
            Console.WriteLine("  " + result.Name.PadRight(14) + result.MedianMilliseconds.ToString("N2", CultureInfo.InvariantCulture).PadLeft(10) + " ms median, " +
                (result.PeakWorkingSetBytes / 1048576.0).ToString("N2", CultureInfo.InvariantCulture).PadLeft(8) + " MiB peak");
        }

        private static string Quote(string value) { return "\"" + value.Replace("\"", "\\\"") + "\""; }
        private static bool Matches(string name, string filter) { return string.IsNullOrEmpty(filter) || name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0; }

        private static void RecreateDirectory(string path)
        {
            if (Directory.Exists(path)) Directory.Delete(path, true);
            Directory.CreateDirectory(path);
        }

        private sealed class ProcessMeasurement
        {
            internal int ExitCode;
            internal double ElapsedMilliseconds;
            internal long PeakWorkingSetBytes;
            internal string StandardError;
        }
    }
}