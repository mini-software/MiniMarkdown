using System;
using System.IO;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;

namespace MiniMarkdown.WebAssembly
{
    [SupportedOSPlatform("browser")]
    internal static partial class Program
    {
        private const int MaximumBrowserPackageBytes = 16 * 1024 * 1024;

        private static void Main()
        {
            Console.WriteLine("MiniMarkdown C# WebAssembly AOT is ready.");
        }

        [JSExport]
        internal static string ConvertXlsx(byte[] input)
        {
            if (input == null)
            {
                throw new ArgumentNullException(nameof(input));
            }

            if (input.Length > MaximumBrowserPackageBytes)
            {
                throw new InvalidDataException("The browser demo accepts XLSX packages up to 16 MiB.");
            }

            using (MemoryStream package = new MemoryStream(input, false))
            using (StringWriter output = new StringWriter())
            {
                new XlsxConverter().Convert(
                    package,
                    output,
                    new ConversionOptions { MaximumPackageBytes = MaximumBrowserPackageBytes });
                return output.ToString().Replace("\r\n", "\n").Replace("\n", "\r\n");
            }
        }
    }
}