using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml;

namespace MiniMarkdown
{
    /// <summary>Converts XLSX workbooks to deterministic Markdown tables.</summary>
    public sealed class XlsxConverter
    {
        /// <summary>Converts an XLSX stream and writes Markdown incrementally.</summary>
        /// <param name="input">The XLSX input stream. The caller retains ownership.</param>
        /// <param name="output">The Markdown writer. The caller retains ownership.</param>
        /// <param name="options">Optional conversion resource limits.</param>
        public void Convert(Stream input, TextWriter output, ConversionOptions options = null)
        {
            if (input == null)
            {
                throw new ArgumentNullException(nameof(input));
            }

            if (output == null)
            {
                throw new ArgumentNullException(nameof(output));
            }

            options = options ?? new ConversionOptions();
            ValidateOptions(options);
            Stream packageStream = input;
            string temporaryPackage = null;
            if (!input.CanSeek)
            {
                temporaryPackage = Path.GetTempFileName();
                using (FileStream file = File.Create(temporaryPackage))
                {
                    CopyWithLimit(input, file, options.MaximumPackageBytes);
                }

                packageStream = File.OpenRead(temporaryPackage);
            }

            try
            {
                if (packageStream.CanSeek && packageStream.Length > options.MaximumPackageBytes)
                {
                    throw new InvalidDataException("The XLSX package exceeds the compressed size limit.");
                }

                using (ZipArchive archive = new ZipArchive(packageStream, ZipArchiveMode.Read, input.CanSeek))
                {
                    ValidateArchive(archive, options);
                    WorkbookInfo workbook = WorkbookReader.Read(archive);
                    CellStyleStore styles = CellStyleStore.Load(archive, workbook.Uses1904DateSystem);
                    using (SharedStringStore strings = SharedStringStore.Load(archive))
                    {
                        bool wroteSheet = false;
                        foreach (SheetInfo sheet in workbook.Sheets)
                        {
                            ZipArchiveEntry entry = archive.GetEntry(sheet.Path);
                            if (entry == null)
                            {
                                throw new InvalidDataException("Worksheet part was not found: " + sheet.Path);
                            }

                            WorksheetBounds bounds = WorksheetReader.Scan(entry, options);
                            if (bounds.IsEmpty)
                            {
                                continue;
                            }

                            if (wroteSheet)
                            {
                                output.WriteLine();
                            }

                            output.Write("## ");
                            output.WriteLine(EscapeHeading(sheet.Name));
                            output.WriteLine();
                            WorksheetReader.Write(entry, bounds, strings, styles, output, options);
                            wroteSheet = true;
                        }
                    }
                }
            }
            finally
            {
                if (!ReferenceEquals(packageStream, input))
                {
                    packageStream.Dispose();
                }

                if (temporaryPackage != null)
                {
                    File.Delete(temporaryPackage);
                }
            }
        }

        /// <summary>Converts an XLSX file to a Markdown file.</summary>
        /// <param name="inputPath">The XLSX input path.</param>
        /// <param name="outputPath">The Markdown output path.</param>
        /// <param name="options">Optional conversion resource limits.</param>
        public void Convert(string inputPath, string outputPath, ConversionOptions options = null)
        {
            using (FileStream input = File.OpenRead(inputPath))
            using (StreamWriter output = new StreamWriter(outputPath, false, new UTF8Encoding(false)))
            {
                Convert(input, output, options);
            }
        }

        private static void ValidateArchive(ZipArchive archive, ConversionOptions options)
        {
            if (archive.Entries.Count > options.MaximumZipEntries)
            {
                throw new InvalidDataException("The XLSX package exceeds the ZIP entry limit.");
            }

            long total = 0;
            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                total += entry.Length;
                if (total > options.MaximumUncompressedBytes)
                {
                    throw new InvalidDataException("The XLSX package exceeds the uncompressed size limit.");
                }

                if (entry.Length > 0 && (entry.CompressedLength == 0 || (double)entry.Length / entry.CompressedLength > options.MaximumCompressionRatio))
                {
                    throw new InvalidDataException("An XLSX entry exceeds the compression ratio limit.");
                }
            }
        }

        private static void ValidateOptions(ConversionOptions options)
        {
            if (options.MaximumColumns < 1 || options.MaximumRows < 1 || options.MaximumUncompressedBytes < 1 ||
                options.MaximumPackageBytes < 1 || options.MaximumZipEntries < 1 || options.MaximumCompressionRatio < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(options), "All conversion limits must be positive.");
            }
        }

        private static void CopyWithLimit(Stream input, Stream output, long limit)
        {
            byte[] buffer = new byte[81920];
            long total = 0;
            int read;
            while ((read = input.Read(buffer, 0, buffer.Length)) != 0)
            {
                total += read;
                if (total > limit)
                {
                    throw new InvalidDataException("The XLSX package exceeds the compressed size limit.");
                }

                output.Write(buffer, 0, read);
            }
        }

        private static string EscapeHeading(string value)
        {
            return value.Replace("\\", "\\\\").Replace("#", "\\#");
        }
    }
}