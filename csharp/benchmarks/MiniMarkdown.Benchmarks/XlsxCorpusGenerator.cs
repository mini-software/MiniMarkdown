using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml;

namespace MiniMarkdown.Benchmarks
{
    internal sealed class BenchmarkCase
    {
        internal string Name;
        internal string Description;
        internal Action<string> Generate;
    }

    internal sealed class SheetDefinition
    {
        internal string Name;
        internal Action<XmlWriter> WriteRows;
    }

    internal static class XlsxCorpusGenerator
    {
        private const string SpreadsheetNamespace = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        internal static int Generate(string outputDirectory, string filter)
        {
            Directory.CreateDirectory(outputDirectory);
            int count = 0;
            foreach (BenchmarkCase benchmarkCase in CreateCases())
            {
                if (!Matches(benchmarkCase.Name, filter))
                {
                    continue;
                }

                string path = Path.Combine(outputDirectory, benchmarkCase.Name + ".xlsx");
                benchmarkCase.Generate(path);
                Console.WriteLine("  [OK] " + Path.GetFileName(path) + " - " + benchmarkCase.Description);
                count++;
            }

            return count;
        }

        internal static IList<BenchmarkCase> CreateCases()
        {
            return new List<BenchmarkCase>
            {
                Case("01_basic_mixed_100x5", "Small mixed table baseline", delegate(string path)
                {
                    CreateWorkbook(path, null, false, Sheet("Data", delegate(XmlWriter writer)
                    {
                        WriteHeader(writer, 1, "Id", "Name", "Amount", "Active", "Note");
                        for (int row = 2; row <= 101; row++)
                        {
                            BeginRow(writer, row);
                            NumberCell(writer, 1, row, (row - 1).ToString(CultureInfo.InvariantCulture));
                            InlineCell(writer, 2, row, "Item " + (row - 1));
                            NumberCell(writer, 3, row, ((row - 1) * 1.25).ToString(CultureInfo.InvariantCulture));
                            BooleanCell(writer, 4, row, row % 2 == 0);
                            InlineCell(writer, 5, row, row % 10 == 0 ? "pipe | newline\nvalue" : "plain");
                            EndRow(writer);
                        }
                    }));
                }),
                Case("02_multi_sheet_4x2500", "Four worksheets with 10,000 total data rows", delegate(string path)
                {
                    List<SheetDefinition> sheets = new List<SheetDefinition>();
                    for (int sheetIndex = 1; sheetIndex <= 4; sheetIndex++)
                    {
                        int capturedSheet = sheetIndex;
                        sheets.Add(Sheet("Region " + sheetIndex, delegate(XmlWriter writer)
                        {
                            WriteHeader(writer, 1, "Region", "Sequence", "Revenue", "Category");
                            for (int row = 2; row <= 2501; row++)
                            {
                                BeginRow(writer, row);
                                InlineCell(writer, 1, row, "R" + capturedSheet);
                                NumberCell(writer, 2, row, (row - 1).ToString(CultureInfo.InvariantCulture));
                                NumberCell(writer, 3, row, (capturedSheet * row * 3.75).ToString(CultureInfo.InvariantCulture));
                                InlineCell(writer, 4, row, "Category " + (row % 17));
                                EndRow(writer);
                            }
                        }));
                    }

                    CreateWorkbook(path, null, false, sheets.ToArray());
                }),
                Case("03_wide_200x100", "Wide worksheet with 100 columns", delegate(string path)
                {
                    CreateWorkbook(path, null, false, Sheet("Wide", delegate(XmlWriter writer)
                    {
                        BeginRow(writer, 1);
                        for (int column = 1; column <= 100; column++)
                        {
                            InlineCell(writer, column, 1, "Column " + column);
                        }
                        EndRow(writer);
                        for (int row = 2; row <= 201; row++)
                        {
                            BeginRow(writer, row);
                            for (int column = 1; column <= 100; column++)
                            {
                                NumberCell(writer, column, row, (row * column).ToString(CultureInfo.InvariantCulture));
                            }
                            EndRow(writer);
                        }
                    }));
                }),
                Case("04_sparse_10000_rows", "Sparse cells with retained internal row gaps", delegate(string path)
                {
                    CreateWorkbook(path, null, false, Sheet("Sparse", delegate(XmlWriter writer)
                    {
                        WriteHeader(writer, 1, "Key", "", "", "", "Value");
                        for (int row = 2; row <= 10000; row += 97)
                        {
                            BeginRow(writer, row);
                            InlineCell(writer, 1, row, "Row " + row);
                            NumberCell(writer, 5, row, (row * 11).ToString(CultureInfo.InvariantCulture));
                            EndRow(writer);
                        }
                    }));
                }),
                Case("05_shared_strings_50000x5", "Shared-string lookup pressure with 250,000 cells", delegate(string path)
                {
                    List<string> strings = new List<string>();
                    for (int index = 0; index < 256; index++)
                    {
                        strings.Add("Shared value " + index.ToString("D3", CultureInfo.InvariantCulture));
                    }

                    CreateWorkbook(path, strings, false, Sheet("Shared", delegate(XmlWriter writer)
                    {
                        BeginRow(writer, 1);
                        for (int column = 1; column <= 5; column++)
                        {
                            SharedCell(writer, column, 1, column - 1);
                        }
                        EndRow(writer);
                        for (int row = 2; row <= 50001; row++)
                        {
                            BeginRow(writer, row);
                            for (int column = 1; column <= 5; column++)
                            {
                                SharedCell(writer, column, row, (row * 7 + column * 31) % strings.Count);
                            }
                            EndRow(writer);
                        }
                    }));
                }),
                Case("06_long_text_5000x4", "Long text and Markdown escaping workload", delegate(string path)
                {
                    string repeated = new string('x', 1024);
                    CreateWorkbook(path, null, false, Sheet("Long Text", delegate(XmlWriter writer)
                    {
                        WriteHeader(writer, 1, "Id", "Payload", "Escaped", "Tail");
                        for (int row = 2; row <= 5001; row++)
                        {
                            BeginRow(writer, row);
                            NumberCell(writer, 1, row, (row - 1).ToString(CultureInfo.InvariantCulture));
                            InlineCell(writer, 2, row, repeated + row);
                            InlineCell(writer, 3, row, "left|right\\path\nline " + row);
                            InlineCell(writer, 4, row, new string((char)('A' + row % 26), 128));
                            EndRow(writer);
                        }
                    }));
                }),
                Case("07_dates_formulas_10000x5", "Date styles, booleans, errors, and cached formulas", delegate(string path)
                {
                    CreateWorkbook(path, null, true, Sheet("Calculations", delegate(XmlWriter writer)
                    {
                        WriteHeader(writer, 1, "Date", "DateTime", "Duration", "Formula", "Error");
                        for (int row = 2; row <= 10001; row++)
                        {
                            double serial = 45000 + (row % 365) + (row % 24) / 24.0;
                            BeginRow(writer, row);
                            StyledNumberCell(writer, 1, row, 1, Math.Floor(serial).ToString(CultureInfo.InvariantCulture));
                            StyledNumberCell(writer, 2, row, 2, serial.ToString("R", CultureInfo.InvariantCulture));
                            StyledNumberCell(writer, 3, row, 3, ((row % 100) / 24.0).ToString("R", CultureInfo.InvariantCulture));
                            FormulaCell(writer, 4, row, "A" + row + "+1", (serial + 1).ToString("R", CultureInfo.InvariantCulture));
                            ErrorCell(writer, 5, row, row % 2 == 0 ? "#DIV/0!" : "#N/A");
                            EndRow(writer);
                        }
                    }));
                }),
                Case("08_tall_100000x5", "Large streaming worksheet with 100,000 data rows", delegate(string path)
                {
                    CreateWorkbook(path, null, false, Sheet("Tall", delegate(XmlWriter writer)
                    {
                        WriteHeader(writer, 1, "Id", "Group", "Value", "Flag", "Description");
                        for (int row = 2; row <= 100001; row++)
                        {
                            BeginRow(writer, row);
                            NumberCell(writer, 1, row, (row - 1).ToString(CultureInfo.InvariantCulture));
                            InlineCell(writer, 2, row, "G" + (row % 101));
                            NumberCell(writer, 3, row, (row * 0.125).ToString(CultureInfo.InvariantCulture));
                            BooleanCell(writer, 4, row, row % 3 == 0);
                            InlineCell(writer, 5, row, "Record " + row + " checksum " + ((row * 7919) % 104729));
                            EndRow(writer);
                        }
                    }));
                })
            };
        }

        private static BenchmarkCase Case(string name, string description, Action<string> generate)
        {
            return new BenchmarkCase { Name = name, Description = description, Generate = generate };
        }

        private static SheetDefinition Sheet(string name, Action<XmlWriter> writeRows)
        {
            return new SheetDefinition { Name = name, WriteRows = writeRows };
        }

        private static bool Matches(string name, string filter)
        {
            return string.IsNullOrEmpty(filter) || name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void CreateWorkbook(string path, IList<string> sharedStrings, bool includeDateStyles, params SheetDefinition[] sheets)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            using (FileStream file = File.Create(path))
            using (ZipArchive archive = new ZipArchive(file, ZipArchiveMode.Create))
            {
                WriteTextEntry(archive, "[Content_Types].xml", ContentTypes(sheets.Length, sharedStrings != null, includeDateStyles));
                WriteTextEntry(archive, "_rels/.rels", "<?xml version=\"1.0\" encoding=\"utf-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>");
                WriteWorkbook(archive, sheets);
                WriteWorkbookRelationships(archive, sheets.Length, sharedStrings != null, includeDateStyles);
                if (sharedStrings != null)
                {
                    WriteSharedStrings(archive, sharedStrings);
                }

                if (includeDateStyles)
                {
                    WriteTextEntry(archive, "xl/styles.xml", "<?xml version=\"1.0\" encoding=\"utf-8\"?><styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><numFmts count=\"1\"><numFmt numFmtId=\"165\" formatCode=\"[h]:mm:ss\"/></numFmts><fonts count=\"1\"><font><sz val=\"11\"/><name val=\"Calibri\"/><family val=\"2\"/><scheme val=\"minor\"/></font></fonts><fills count=\"2\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill></fills><borders count=\"1\"><border><left/><right/><top/><bottom/><diagonal/></border></borders><cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs><cellXfs count=\"4\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/><xf numFmtId=\"14\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/><xf numFmtId=\"22\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/><xf numFmtId=\"165\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/></cellXfs><cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles></styleSheet>");
                }

                for (int index = 0; index < sheets.Length; index++)
                {
                    WriteWorksheet(archive, "xl/worksheets/sheet" + (index + 1) + ".xml", sheets[index].WriteRows);
                }
            }
        }

        private static string ContentTypes(int sheetCount, bool sharedStrings, bool styles)
        {
            StringBuilder value = new StringBuilder("<?xml version=\"1.0\" encoding=\"utf-8\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>");
            for (int index = 1; index <= sheetCount; index++)
            {
                value.Append("<Override PartName=\"/xl/worksheets/sheet").Append(index).Append(".xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>");
            }
            if (sharedStrings) value.Append("<Override PartName=\"/xl/sharedStrings.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sharedStrings+xml\"/>");
            if (styles) value.Append("<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>");
            return value.Append("</Types>").ToString();
        }

        private static void WriteWorkbook(ZipArchive archive, SheetDefinition[] sheets)
        {
            using (XmlWriter writer = CreateXmlWriter(archive.CreateEntry("xl/workbook.xml", CompressionLevel.Optimal).Open()))
            {
                writer.WriteStartDocument();
                writer.WriteStartElement("workbook", SpreadsheetNamespace);
                writer.WriteAttributeString("xmlns", "r", null, "http://schemas.openxmlformats.org/officeDocument/2006/relationships");
                writer.WriteStartElement("sheets", SpreadsheetNamespace);
                for (int index = 0; index < sheets.Length; index++)
                {
                    writer.WriteStartElement("sheet", SpreadsheetNamespace);
                    writer.WriteAttributeString("name", sheets[index].Name);
                    writer.WriteAttributeString("sheetId", (index + 1).ToString(CultureInfo.InvariantCulture));
                    writer.WriteAttributeString("r", "id", "http://schemas.openxmlformats.org/officeDocument/2006/relationships", "rId" + (index + 1));
                    writer.WriteEndElement();
                }
                writer.WriteEndElement();
                writer.WriteEndElement();
            }
        }

        private static void WriteWorkbookRelationships(ZipArchive archive, int sheetCount, bool sharedStrings, bool styles)
        {
            StringBuilder value = new StringBuilder("<?xml version=\"1.0\" encoding=\"utf-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">");
            for (int index = 1; index <= sheetCount; index++)
            {
                value.Append("<Relationship Id=\"rId").Append(index).Append("\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet").Append(index).Append(".xml\"/>");
            }
            int relationship = sheetCount + 1;
            if (sharedStrings) value.Append("<Relationship Id=\"rId").Append(relationship++).Append("\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/sharedStrings\" Target=\"sharedStrings.xml\"/>");
            if (styles) value.Append("<Relationship Id=\"rId").Append(relationship).Append("\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>");
            WriteTextEntry(archive, "xl/_rels/workbook.xml.rels", value.Append("</Relationships>").ToString());
        }

        private static void WriteSharedStrings(ZipArchive archive, IList<string> strings)
        {
            using (XmlWriter writer = CreateXmlWriter(archive.CreateEntry("xl/sharedStrings.xml", CompressionLevel.Optimal).Open()))
            {
                writer.WriteStartDocument();
                writer.WriteStartElement("sst", SpreadsheetNamespace);
                writer.WriteAttributeString("count", strings.Count.ToString(CultureInfo.InvariantCulture));
                writer.WriteAttributeString("uniqueCount", strings.Count.ToString(CultureInfo.InvariantCulture));
                foreach (string value in strings)
                {
                    writer.WriteStartElement("si", SpreadsheetNamespace);
                    writer.WriteElementString("t", SpreadsheetNamespace, value);
                    writer.WriteEndElement();
                }
                writer.WriteEndElement();
            }
        }

        private static void WriteWorksheet(ZipArchive archive, string path, Action<XmlWriter> writeRows)
        {
            using (XmlWriter writer = CreateXmlWriter(archive.CreateEntry(path, CompressionLevel.Fastest).Open()))
            {
                writer.WriteStartDocument();
                writer.WriteStartElement("worksheet", SpreadsheetNamespace);
                writer.WriteStartElement("sheetData", SpreadsheetNamespace);
                writeRows(writer);
                writer.WriteEndElement();
                writer.WriteEndElement();
            }
        }

        private static XmlWriter CreateXmlWriter(Stream stream)
        {
            return XmlWriter.Create(stream, new XmlWriterSettings { Encoding = new UTF8Encoding(false), CloseOutput = true, Indent = false });
        }

        private static void WriteTextEntry(ZipArchive archive, string path, string content)
        {
            using (Stream stream = archive.CreateEntry(path, CompressionLevel.Optimal).Open())
            using (StreamWriter writer = new StreamWriter(stream, new UTF8Encoding(false)))
            {
                writer.Write(content);
            }
        }

        private static void WriteHeader(XmlWriter writer, int row, params string[] values)
        {
            BeginRow(writer, row);
            for (int column = 0; column < values.Length; column++) InlineCell(writer, column + 1, row, values[column]);
            EndRow(writer);
        }

        private static void BeginRow(XmlWriter writer, int row)
        {
            writer.WriteStartElement("row", SpreadsheetNamespace);
            writer.WriteAttributeString("r", row.ToString(CultureInfo.InvariantCulture));
        }

        private static void EndRow(XmlWriter writer) { writer.WriteEndElement(); }

        private static void InlineCell(XmlWriter writer, int column, int row, string value)
        {
            BeginCell(writer, column, row, "inlineStr", null);
            writer.WriteStartElement("is", SpreadsheetNamespace);
            writer.WriteElementString("t", SpreadsheetNamespace, value);
            writer.WriteEndElement();
            writer.WriteEndElement();
        }

        private static void SharedCell(XmlWriter writer, int column, int row, int index)
        {
            BeginCell(writer, column, row, "s", null);
            writer.WriteElementString("v", SpreadsheetNamespace, index.ToString(CultureInfo.InvariantCulture));
            writer.WriteEndElement();
        }

        private static void NumberCell(XmlWriter writer, int column, int row, string value) { StyledNumberCell(writer, column, row, 0, value); }

        private static void StyledNumberCell(XmlWriter writer, int column, int row, int style, string value)
        {
            BeginCell(writer, column, row, null, style == 0 ? null : style.ToString(CultureInfo.InvariantCulture));
            writer.WriteElementString("v", SpreadsheetNamespace, value);
            writer.WriteEndElement();
        }

        private static void BooleanCell(XmlWriter writer, int column, int row, bool value)
        {
            BeginCell(writer, column, row, "b", null);
            writer.WriteElementString("v", SpreadsheetNamespace, value ? "1" : "0");
            writer.WriteEndElement();
        }

        private static void ErrorCell(XmlWriter writer, int column, int row, string value)
        {
            BeginCell(writer, column, row, "e", null);
            writer.WriteElementString("v", SpreadsheetNamespace, value);
            writer.WriteEndElement();
        }

        private static void FormulaCell(XmlWriter writer, int column, int row, string formula, string cachedValue)
        {
            BeginCell(writer, column, row, null, null);
            writer.WriteElementString("f", SpreadsheetNamespace, formula);
            writer.WriteElementString("v", SpreadsheetNamespace, cachedValue);
            writer.WriteEndElement();
        }

        private static void BeginCell(XmlWriter writer, int column, int row, string type, string style)
        {
            writer.WriteStartElement("c", SpreadsheetNamespace);
            writer.WriteAttributeString("r", ColumnName(column) + row.ToString(CultureInfo.InvariantCulture));
            if (type != null) writer.WriteAttributeString("t", type);
            if (style != null) writer.WriteAttributeString("s", style);
        }

        private static string ColumnName(int column)
        {
            StringBuilder value = new StringBuilder();
            while (column > 0)
            {
                column--;
                value.Insert(0, (char)('A' + column % 26));
                column /= 26;
            }
            return value.ToString();
        }
    }
}