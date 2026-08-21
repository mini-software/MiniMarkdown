using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Xml;

namespace MiniMarkdown
{
    internal sealed class SheetInfo
    {
        internal string Name;
        internal string Path;
    }

    internal sealed class WorkbookInfo
    {
        internal readonly List<SheetInfo> Sheets = new List<SheetInfo>();
        internal bool Uses1904DateSystem;
    }

    internal static class WorkbookReader
    {
        private const string RelationshipNamespace = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

        internal static WorkbookInfo Read(ZipArchive archive)
        {
            ZipArchiveEntry workbookEntry = archive.GetEntry("xl/workbook.xml");
            ZipArchiveEntry relationshipsEntry = archive.GetEntry("xl/_rels/workbook.xml.rels");
            if (workbookEntry == null || relationshipsEntry == null)
            {
                throw new InvalidDataException("The file is not a valid XLSX workbook.");
            }

            Dictionary<string, string> relationships = ReadRelationships(relationshipsEntry);
            WorkbookInfo result = new WorkbookInfo();
            using (Stream stream = workbookEntry.Open())
            using (XmlReader reader = XmlReader.Create(stream, XmlSettings.Create()))
            {
                while (reader.Read())
                {
                    if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "workbookPr")
                    {
                        string date1904 = reader.GetAttribute("date1904");
                        result.Uses1904DateSystem = date1904 == "1" || string.Equals(date1904, "true", StringComparison.OrdinalIgnoreCase);
                    }

                    if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "sheet")
                    {
                        continue;
                    }

                    string relationshipId = reader.GetAttribute("id", RelationshipNamespace);
                    string target;
                    if (relationshipId == null || !relationships.TryGetValue(relationshipId, out target))
                    {
                        throw new InvalidDataException("A worksheet relationship is missing.");
                    }

                    result.Sheets.Add(new SheetInfo { Name = reader.GetAttribute("name") ?? "Sheet", Path = target });
                }
            }

            return result;
        }

        private static Dictionary<string, string> ReadRelationships(ZipArchiveEntry entry)
        {
            Dictionary<string, string> result = new Dictionary<string, string>(StringComparer.Ordinal);
            using (Stream stream = entry.Open())
            using (XmlReader reader = XmlReader.Create(stream, XmlSettings.Create()))
            {
                while (reader.Read())
                {
                    if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "Relationship")
                    {
                        continue;
                    }

                    if (string.Equals(reader.GetAttribute("TargetMode"), "External", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    string id = reader.GetAttribute("Id");
                    string target = reader.GetAttribute("Target");
                    if (id != null && target != null)
                    {
                        result[id] = ResolvePartPath(target);
                    }
                }
            }

            return result;
        }

        private static string ResolvePartPath(string target)
        {
            string normalized = target.Replace('\\', '/');
            if (normalized.StartsWith("/", StringComparison.Ordinal))
            {
                normalized = normalized.Substring(1);
            }
            else
            {
                normalized = "xl/" + normalized;
            }

            string[] parts = normalized.Split('/');
            List<string> safeParts = new List<string>();
            foreach (string part in parts)
            {
                if (part.Length == 0 || part == ".")
                {
                    continue;
                }

                if (part == "..")
                {
                    if (safeParts.Count == 0)
                    {
                        throw new InvalidDataException("A package relationship escapes the XLSX package.");
                    }

                    safeParts.RemoveAt(safeParts.Count - 1);
                    continue;
                }

                safeParts.Add(part);
            }

            return string.Join("/", safeParts.ToArray());
        }
    }

    internal static class XmlSettings
    {
        internal static XmlReaderSettings Create()
        {
            return new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, IgnoreComments = true };
        }
    }
}