namespace MiniMarkdown
{
    /// <summary>Controls resource limits applied during XLSX conversion.</summary>
    public sealed class ConversionOptions
    {
        /// <summary>Gets or sets the maximum number of columns in a worksheet.</summary>
        public int MaximumColumns { get; set; } = 16384;

        /// <summary>Gets or sets the maximum number of rows in a worksheet.</summary>
        public int MaximumRows { get; set; } = 1048576;

        /// <summary>Gets or sets the maximum total uncompressed ZIP entry size.</summary>
        public long MaximumUncompressedBytes { get; set; } = 512L * 1024 * 1024;

        /// <summary>Gets or sets the maximum compressed XLSX package size.</summary>
        public long MaximumPackageBytes { get; set; } = 256L * 1024 * 1024;

        /// <summary>Gets or sets the maximum number of ZIP entries.</summary>
        public int MaximumZipEntries { get; set; } = 10000;

        /// <summary>Gets or sets the maximum uncompressed-to-compressed ratio for an entry.</summary>
        public double MaximumCompressionRatio { get; set; } = 1000;
    }
}