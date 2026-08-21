# MiniMarkdown for C#

MiniMarkdown converts XLSX workbooks to deterministic GitHub-Flavored Markdown with bounded memory. It uses only .NET BCL APIs and has no NuGet dependencies.

## Targets

- Library: .NET Standard 2.0 and .NET Framework 4.6.2
- CLI and test executables: .NET Framework 4.6.2

## Usage

```csharp
using (Stream input = File.OpenRead("report.xlsx"))
using (TextWriter output = File.CreateText("report.md"))
{
    new MiniMarkdown.XlsxConverter().Convert(input, output);
}
```

```powershell
minimarkdown report.xlsx -o report.md
minimarkdown report.xlsx
Get-Content report.xlsx -AsByteStream | minimarkdown -
```

Caller-owned streams remain open. Non-seekable input is copied to a temporary file because XLSX ZIP entries must be revisited.

## Output contract

- Each non-empty worksheet starts with a level-two heading.
- The first effective row is the Markdown table header.
- Empty outer rows and columns are removed; internal gaps are retained.
- Backslashes and pipes are escaped, and cell newlines become `<br>`.
- Cached formula results are emitted; formulas are not calculated.
- Dates and times use invariant ISO-like forms.
- Merged cells keep the top-left value and leave covered cells empty.

## Validation

```powershell
dotnet build csharp/MiniMarkdown.sln
dotnet run --project csharp/tests/MiniMarkdown.Tests
dotnet run --project csharp/tests/MiniMarkdown.Comparison -- sample.xlsx
```

Run the multi-case performance and semantic comparison benchmark with:

```powershell
.\scripts\Run-Benchmark.ps1
```

See [benchmarks/README.md](benchmarks/README.md) for cases, metrics, and reference-tool configuration.

The comparison runner invokes `anydoc` and `markitdown` from `PATH`. Override their executable names with `ANYDOC_COMMAND` and `MARKITDOWN_COMMAND`. It reports semantic document shape rather than requiring byte-identical Markdown because the tools use different heading, date, empty-cell, and table policies. MiniMarkdown's exact-output tests are authoritative.

## Current scope

XLSX shared strings, inline strings, numbers, booleans, errors, cached formula values, common date/time styles, sparse cells, and multiple worksheets are supported. Legacy XLS, XLSB, images, charts, OCR, and formula evaluation are outside the first release.