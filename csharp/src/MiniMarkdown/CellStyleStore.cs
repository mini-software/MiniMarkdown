using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Xml;

namespace MiniMarkdown
{
    internal enum CellNumberKind
    {
        Number,
        Date,
        DateTime,
        Time,
        Duration
    }

    internal sealed class CellStyleStore
    {
        private static readonly Regex QuotedText = new Regex("\\\"[^\\\"]*\\\"", RegexOptions.Compiled);
        private readonly List<CellNumberKind> styles = new List<CellNumberKind>();
        private readonly bool uses1904DateSystem;

        private CellStyleStore(bool uses1904DateSystem)
        {
            this.uses1904DateSystem = uses1904DateSystem;
            styles.Add(CellNumberKind.Number);
        }

        internal static CellStyleStore Load(ZipArchive archive, bool uses1904DateSystem)
        {
            CellStyleStore store = new CellStyleStore(uses1904DateSystem);
            ZipArchiveEntry entry = archive.GetEntry("xl/styles.xml");
            if (entry == null)
            {
                return store;
            }

            Dictionary<int, string> customFormats = new Dictionary<int, string>();
            using (Stream stream = entry.Open())
            using (XmlReader reader = XmlReader.Create(stream, XmlSettings.Create()))
            {
                bool inCellFormats = false;
                while (reader.Read())
                {
                    if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "numFmt")
                    {
                        int id;
                        if (int.TryParse(reader.GetAttribute("numFmtId"), NumberStyles.None, CultureInfo.InvariantCulture, out id))
                        {
                            customFormats[id] = reader.GetAttribute("formatCode") ?? string.Empty;
                        }
                    }
                    else if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "cellXfs")
                    {
                        inCellFormats = true;
                    }
                    else if (reader.NodeType == XmlNodeType.EndElement && reader.LocalName == "cellXfs")
                    {
                        inCellFormats = false;
                    }
                    else if (inCellFormats && reader.NodeType == XmlNodeType.Element && reader.LocalName == "xf")
                    {
                        int formatId;
                        int.TryParse(reader.GetAttribute("numFmtId"), NumberStyles.None, CultureInfo.InvariantCulture, out formatId);
                        string format;
                        customFormats.TryGetValue(formatId, out format);
                        store.styles.Add(Classify(formatId, format));
                    }
                }
            }

            if (store.styles.Count > 1)
            {
                store.styles.RemoveAt(0);
            }

            return store;
        }

        internal string Format(int styleIndex, double value, string original)
        {
            if (styleIndex < 0 || styleIndex >= styles.Count)
            {
                return original;
            }

            CellNumberKind kind = styles[styleIndex];
            if (kind == CellNumberKind.Number)
            {
                return original;
            }

            if (kind == CellNumberKind.Duration)
            {
                TimeSpan duration = TimeSpan.FromDays(value);
                return ((long)duration.TotalHours).ToString(CultureInfo.InvariantCulture) + duration.ToString(@"\:mm\:ss", CultureInfo.InvariantCulture);
            }

            DateTime date = uses1904DateSystem
                ? new DateTime(1904, 1, 1).AddDays(value)
                : DateTime.FromOADate(value);
            if (kind == CellNumberKind.Date)
            {
                return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            }

            if (kind == CellNumberKind.Time)
            {
                return date.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
            }

            return date.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        }

        private static CellNumberKind Classify(int id, string format)
        {
            if (id >= 14 && id <= 17)
            {
                return CellNumberKind.Date;
            }

            if (id == 46)
            {
                return CellNumberKind.Duration;
            }

            if (id == 18 || id == 19 || id == 20 || id == 21 || id == 45 || id == 47)
            {
                return CellNumberKind.Time;
            }

            if (id == 22)
            {
                return CellNumberKind.DateTime;
            }

            string code = QuotedText.Replace(format ?? string.Empty, string.Empty).ToLowerInvariant();
            if (code.Contains("[h]") || code.Contains("[m]") || code.Contains("[s]"))
            {
                return CellNumberKind.Duration;
            }

            bool hasDate = code.IndexOf('y') >= 0 || code.IndexOf('d') >= 0;
            bool hasTime = code.IndexOf('h') >= 0 || code.IndexOf('s') >= 0;
            if (hasDate && hasTime)
            {
                return CellNumberKind.DateTime;
            }

            return hasDate ? CellNumberKind.Date : hasTime ? CellNumberKind.Time : CellNumberKind.Number;
        }
    }
}