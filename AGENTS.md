# MiniMarkdown Development Rules

- Keep all C# projects and source files under `csharp/`.
- Use English for code, comments, documentation, errors, and tests.
- Do not add third-party libraries or NuGet package dependencies. Use .NET BCL APIs only.
- Keep the library compatible with .NET Standard 2.0 and .NET Framework 4.6.2.
- Keep the CLI and test executables compatible with .NET Framework 4.6.2.
- Preserve streaming, bounded-memory conversion. Do not load an entire workbook, worksheet, or Markdown output into memory.
- Keep caller-owned streams open. Use temporary files for non-seekable XLSX input when entries must be revisited.
- Treat MiniMarkdown's deterministic Markdown output as the authoritative contract. Compare with anydoc and MarkItDown semantically, not byte-for-byte.
- Add dependency-free integration tests for conversion behavior, malformed input, resource limits, and large worksheets.
- Build and test before finishing changes:
  - `dotnet build csharp/MiniMarkdown.sln -c Release`
  - `dotnet run --project csharp/tests/MiniMarkdown.Tests -c Release`
- Do not expand the supported file formats unless the task explicitly requests it. XLSX is the current priority.