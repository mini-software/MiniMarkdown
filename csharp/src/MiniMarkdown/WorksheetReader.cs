using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml;

namespace MiniMarkdown
{
    internal sealed class WorksheetBounds
    {
        internal int FirstRow = int.MaxValue;
        internal int LastRow;
        internal int FirstColumn = int.MaxValue;
        internal int LastColumn;
        internal bool IsEmpty { get { return LastRow == 0; } }
    }

    internal static class WorksheetReader
    {
        internal static WorksheetBounds Scan(ZipArchiveEntry entry, ConversionOptions options)
        {
            WorksheetBounds bounds = new WorksheetBounds();
            VisitCells(entry, options, delegate(int row, int column, string type, int style, string value)
            {
                if (value.Length == 0)
                {
                    return;
                }

                bounds.FirstRow = Math.Min(bounds.FirstRow, row);
                bounds.LastRow = Math.Max(bounds.LastRow, row);
                bounds.FirstColumn = Math.Min(bounds.FirstColumn, column);
                bounds.LastColumn = Math.Max(bounds.LastColumn, column);
            });
            return bounds;
        }

        internal static void Write(ZipArchiveEntry entry, WorksheetBounds bounds, SharedStringStore strings, CellStyleStore styles, TextWriter output, ConversionOptions options)
        {
            SortedDictionary<int, string> rowValues = new SortedDictionary<int, string>();
            int currentRow = bounds.FirstRow;
            bool headerWritten = false;
            VisitCells(entry, options, delegate(int row, int column, string type, int style, string value)
            {
                if (row < bounds.FirstRow || row > bounds.LastRow)
                {
                    return;
                }

                while (row > currentRow)
                {
                    WriteRow(output, rowValues, bounds.FirstColumn, bounds.LastColumn);
                    if (!headerWritten)
                    {
                        WriteSeparator(output, bounds.LastColumn - bounds.FirstColumn + 1);
                        headerWritten = true;
                    }

                    rowValues.Clear();
                    currentRow++;
                }

                rowValues[column] = FormatValue(type, style, value, strings, styles);
            });

            while (currentRow <= bounds.LastRow)
            {
                WriteRow(output, rowValues, bounds.FirstColumn, bounds.LastColumn);
                if (!headerWritten)
                {
                    WriteSeparator(output, bounds.LastColumn - bounds.FirstColumn + 1);
                    headerWritten = true;
                }

                rowValues.Clear();
                currentRow++;
            }
        }

        private static void VisitCells(ZipArchiveEntry entry, ConversionOptions options, Action<int, int, string, int, string> visitor)
        {
            using (Stream stream = entry.Open())
            using (XmlReader reader = XmlReader.Create(stream, XmlSettings.Create()))
            {
                int inferredRow = 0;
                int currentRow = 0;
                int inferredColumn = 0;
                while (reader.Read())
                {
                    if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "row")
                    {
                        inferredRow++;
                        currentRow = ParsePositiveInt(reader.GetAttribute("r"), inferredRow);
                        inferredRow = currentRow;
                        inferredColumn = 0;
                        if (currentRow > options.MaximumRows)
                        {
                            throw new InvalidDataException("The worksheet exceeds the row limit.");
                        }
                    }
                    else if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "c")
                    {
                        string reference = reader.GetAttribute("r");
                        int column = reference == null ? inferredColumn + 1 : ParseColumn(reference);
                        inferredColumn = column;
                        if (column > options.MaximumColumns)
                        {
                            throw new InvalidDataException("The worksheet exceeds the column limit.");
                        }

                        string type = reader.GetAttribute("t") ?? string.Empty;
                        int style = ParsePositiveInt(reader.GetAttribute("s"), 0);
                        string value = ReadCell(reader.ReadSubtree(), type);
                        visitor(currentRow, column, type, style, value);
                    }
                }
            }
        }

        private static string ReadCell(XmlReader reader, string type)
        {
            StringBuilder inline = type == "inlineStr" ? new StringBuilder() : null;
            string value = string.Empty;
            while (reader.Read())
            {
                if (reader.NodeType != XmlNodeType.Element)
                {
                    continue;
                }

                if (inline != null && reader.LocalName == "t")
                {
                    inline.Append(reader.ReadElementContentAsString());
                }
                else if (reader.LocalName == "v")
                {
                    value = reader.ReadElementContentAsString();
                }
            }

            return inline == null ? value : inline.ToString();
        }

        private static string FormatValue(string type, int style, string value, SharedStringStore strings, CellStyleStore styles)
        {
            if (type == "s")
            {
                return strings.Get(value);
            }

            if (type == "b")
            {
                return value == "1" ? "TRUE" : "FALSE";
            }

            double number;
            if (type.Length == 0 && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out number))
            {
                return styles.Format(style, number, value);
            }

            return value;
        }

        private static void WriteRow(TextWriter output, SortedDictionary<int, string> values, int firstColumn, int lastColumn)
        {
            output.Write('|');
            for (int column = firstColumn; column <= lastColumn; column++)
            {
                string value;
                values.TryGetValue(column, out value);
                output.Write(' ');
                output.Write(EscapeCell(value ?? string.Empty));
                output.Write(" |");
            }

            output.WriteLine();
        }

        private static void WriteSeparator(TextWriter output, int columns)
        {
            output.Write('|');
            for (int column = 0; column < columns; column++)
            {
                output.Write(" --- |");
            }

            output.WriteLine();
        }

        private static string EscapeCell(string value)
        {
            return value.Replace("\\", "\\\\").Replace("|", "\\|").Replace("\r\n", "<br>").Replace("\r", "<br>").Replace("\n", "<br>");
        }

        private static int ParsePositiveInt(string text, int fallback)
        {
            int value;
            return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value) && value > 0 ? value : fallback;
        }

        private static int ParseColumn(string reference)
        {
            int column = 0;
            int index = 0;
            while (index < reference.Length && reference[index] >= 'A' && reference[index] <= 'Z')
            {
                column = checked(column * 26 + reference[index] - 'A' + 1);
                index++;
            }

            if (column == 0)
            {
                throw new InvalidDataException("A cell reference is invalid.");
            }

            return column;
        }
    }
}