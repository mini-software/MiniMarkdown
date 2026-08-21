using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace MiniMarkdown.Tests
{
    internal static class Program
    {
        private static int failures;

        private static int Main()
        {
            Run("Converts common cell types and sparse rows", ConvertsCommonCells);
            Run("Preserves caller-owned streams", PreservesInputStream);
            Run("Converts a non-seekable input stream", ConvertsNonSeekableInput);
            Run("Rejects packages over resource limits", RejectsPackageOverLimit);
            Run("Writes a large worksheet incrementally", WritesLargeWorksheetIncrementally);
            Console.WriteLine(failures == 0 ? "All tests passed." : failures + " test(s) failed.");
            return failures == 0 ? 0 : 1;
        }

        private static void ConvertsCommonCells()
        {
            using (MemoryStream package = CreateWorkbook())
            using (StringWriter output = new StringWriter())
            {
                new XlsxConverter().Convert(package, output);
                string expected =
                    "## Data\r\n\r\n" +
                    "| Name | Note |  | Active | Date |\r\n" +
                    "| --- | --- | --- | --- | --- |\r\n" +
                    "| Alice | A\\|B<br>C |  | TRUE | 2023-03-15 |\r\n" +
                    "|  |  | 42.5 |  |  |\r\n";
                Equal(expected, output.ToString());
            }
        }

        private static void PreservesInputStream()
        {
            MemoryStream package = CreateWorkbook();
            new XlsxConverter().Convert(package, TextWriter.Null);
            True(package.CanRead, "The converter disposed the caller-owned input stream.");
            package.Dispose();
        }

        private static void ConvertsNonSeekableInput()
        {
            using (MemoryStream package = CreateWorkbook())
            using (Stream input = new NonSeekableStream(package))
            using (StringWriter output = new StringWriter())
            {
                new XlsxConverter().Convert(input, output);
                True(output.ToString().Contains("| Alice |"), "Non-seekable input produced no table data.");
            }
        }

        private static void RejectsPackageOverLimit()
        {
            using (MemoryStream package = CreateWorkbook())
            {
                Throws<InvalidDataException>(delegate
                {
                    new XlsxConverter().Convert(package, TextWriter.Null, new ConversionOptions { MaximumPackageBytes = 1 });
                });
            }
        }

        private static void WritesLargeWorksheetIncrementally()
        {
            const int rows = 20000;
            using (MemoryStream package = CreateLargeWorkbook(rows))
            using (CountingWriter output = new CountingWriter())
            {
                new XlsxConverter().Convert(package, output);
                True(output.LineCount == rows + 3, "The large worksheet produced an unexpected line count.");
                True(output.MaximumWriteLength < 64, "The converter buffered a large Markdown fragment before writing.");
            }
        }

        private static MemoryStream CreateWorkbook()
        {
            MemoryStream stream = new MemoryStream();
            using (ZipArchive archive = new ZipArchive(stream, ZipArchiveMode.Create, true))
            {
                Add(archive, "[Content_Types].xml", "<?xml version=\"1.0\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/><Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/><Override PartName=\"/xl/sharedStrings.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sharedStrings+xml\"/></Types>");
                Add(archive, "xl/workbook.xml", "<?xml version=\"1.0\"?><workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><workbookPr date1904=\"0\"/><sheets><sheet name=\"Data\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>");
                Add(archive, "xl/_rels/workbook.xml.rels", "<?xml version=\"1.0\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/></Relationships>");
                Add(archive, "xl/sharedStrings.xml", "<?xml version=\"1.0\"?><sst xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><si><t>Name</t></si><si><t>Alice</t></si><si><r><t>A|B</t></r><r><t>\nC</t></r></si></sst>");
                Add(archive, "xl/styles.xml", "<?xml version=\"1.0\"?><styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><cellXfs count=\"2\"><xf numFmtId=\"0\"/><xf numFmtId=\"14\"/></cellXfs></styleSheet>");
                Add(archive, "xl/worksheets/sheet1.xml", "<?xml version=\"1.0\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData><row r=\"1\"><c r=\"A1\" t=\"s\"><v>0</v></c><c r=\"B1\" t=\"inlineStr\"><is><t>Note</t></is></c><c r=\"D1\" t=\"inlineStr\"><is><t>Active</t></is></c><c r=\"E1\" t=\"inlineStr\"><is><t>Date</t></is></c></row><row r=\"2\"><c r=\"A2\" t=\"s\"><v>1</v></c><c r=\"B2\" t=\"s\"><v>2</v></c><c r=\"D2\" t=\"b\"><v>1</v></c><c r=\"E2\" s=\"1\"><v>45000</v></c></row><row r=\"3\"><c r=\"C3\"><v>42.5</v></c></row></sheetData></worksheet>");
            }

            stream.Position = 0;
            return stream;
        }

        private static MemoryStream CreateLargeWorkbook(int rows)
        {
            MemoryStream stream = new MemoryStream();
            using (ZipArchive archive = new ZipArchive(stream, ZipArchiveMode.Create, true))
            {
                Add(archive, "xl/workbook.xml", "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets><sheet name=\"Large\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>");
                Add(archive, "xl/_rels/workbook.xml.rels", "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Target=\"worksheets/sheet1.xml\"/></Relationships>");
                ZipArchiveEntry sheet = archive.CreateEntry("xl/worksheets/sheet1.xml", CompressionLevel.Fastest);
                using (Stream sheetStream = sheet.Open())
                using (StreamWriter writer = new StreamWriter(sheetStream, new UTF8Encoding(false)))
                {
                    writer.Write("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>");
                    for (int row = 1; row <= rows; row++)
                    {
                        writer.Write("<row r=\"");
                        writer.Write(row);
                        writer.Write("\"><c r=\"A");
                        writer.Write(row);
                        writer.Write("\"><v>");
                        writer.Write(row);
                        writer.Write("</v></c></row>");
                    }

                    writer.Write("</sheetData></worksheet>");
                }
            }

            stream.Position = 0;
            return stream;
        }

        private static void Add(ZipArchive archive, string path, string content)
        {
            using (Stream stream = archive.CreateEntry(path).Open())
            using (StreamWriter writer = new StreamWriter(stream, new UTF8Encoding(false)))
            {
                writer.Write(content);
            }
        }

        private static void Run(string name, Action test)
        {
            try
            {
                test();
                Console.WriteLine("PASS " + name);
            }
            catch (Exception exception)
            {
                failures++;
                Console.Error.WriteLine("FAIL " + name + ": " + exception.Message);
            }
        }

        private static void Equal(string expected, string actual)
        {
            if (!string.Equals(expected, actual, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Expected:\n" + expected + "Actual:\n" + actual);
            }
        }

        private static void True(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }

        private static void Throws<T>(Action action) where T : Exception
        {
            try
            {
                action();
            }
            catch (T)
            {
                return;
            }

            throw new InvalidOperationException("Expected exception " + typeof(T).Name + ".");
        }
    }

    internal sealed class NonSeekableStream : Stream
    {
        private readonly Stream inner;

        internal NonSeekableStream(Stream inner)
        {
            this.inner = inner;
        }

        public override bool CanRead { get { return true; } }
        public override bool CanSeek { get { return false; } }
        public override bool CanWrite { get { return false; } }
        public override long Length { get { throw new NotSupportedException(); } }
        public override long Position { get { throw new NotSupportedException(); } set { throw new NotSupportedException(); } }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) { return inner.Read(buffer, offset, count); }
        public override long Seek(long offset, SeekOrigin origin) { throw new NotSupportedException(); }
        public override void SetLength(long value) { throw new NotSupportedException(); }
        public override void Write(byte[] buffer, int offset, int count) { throw new NotSupportedException(); }
    }

    internal sealed class CountingWriter : TextWriter
    {
        internal int LineCount;
        internal int MaximumWriteLength;
        public override Encoding Encoding { get { return Encoding.UTF8; } }

        public override void Write(char value)
        {
            MaximumWriteLength = Math.Max(MaximumWriteLength, 1);
        }

        public override void Write(string value)
        {
            MaximumWriteLength = Math.Max(MaximumWriteLength, value == null ? 0 : value.Length);
        }

        public override void WriteLine()
        {
            LineCount++;
        }

        public override void WriteLine(string value)
        {
            Write(value);
            LineCount++;
        }
    }
}