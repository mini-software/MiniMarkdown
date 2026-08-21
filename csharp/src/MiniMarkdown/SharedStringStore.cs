using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml;

namespace MiniMarkdown
{
    internal sealed class SharedStringStore : IDisposable
    {
        private readonly string path;
        private readonly FileStream stream;
        private readonly List<long> offsets = new List<long>();

        private SharedStringStore()
        {
            path = Path.GetTempFileName();
            stream = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.Read);
        }

        internal static SharedStringStore Load(ZipArchive archive)
        {
            SharedStringStore store = new SharedStringStore();
            ZipArchiveEntry entry = archive.GetEntry("xl/sharedStrings.xml");
            if (entry == null)
            {
                return store;
            }

            using (Stream input = entry.Open())
            using (XmlReader reader = XmlReader.Create(input, XmlSettings.Create()))
            {
                while (reader.Read())
                {
                    if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "si")
                    {
                        store.Write(ReadItem(reader.ReadSubtree()));
                    }
                }
            }

            return store;
        }

        internal string Get(string indexText)
        {
            int index;
            if (!int.TryParse(indexText, NumberStyles.None, CultureInfo.InvariantCulture, out index) || index < 0 || index >= offsets.Count)
            {
                throw new InvalidDataException("A shared string index is invalid.");
            }

            stream.Position = offsets[index];
            using (BinaryReader reader = new BinaryReader(stream, Encoding.UTF8, true))
            {
                return reader.ReadString();
            }
        }

        public void Dispose()
        {
            stream.Dispose();
            File.Delete(path);
        }

        private void Write(string value)
        {
            offsets.Add(stream.Position);
            using (BinaryWriter writer = new BinaryWriter(stream, Encoding.UTF8, true))
            {
                writer.Write(value);
            }
        }

        private static string ReadItem(XmlReader reader)
        {
            StringBuilder value = new StringBuilder();
            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "t")
                {
                    value.Append(reader.ReadElementContentAsString());
                }
            }

            return value.ToString();
        }
    }
}