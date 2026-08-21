---
name: minimarkdown-xlsx
description: "Convert XLSX workbooks to deterministic GitHub-Flavored Markdown with MiniMarkdown. Use when an agent needs to convert Excel .xlsx files, automate XLSX-to-Markdown workflows, compare C#/Rust/Node outputs, preserve sparse tables, or troubleshoot MiniMarkdown conversion and resource limits."
---

# MiniMarkdown XLSX

Use MiniMarkdown when the source is an XLSX workbook and deterministic Markdown tables are required. Do not use it for XLS, XLSB, OCR, formula calculation, charts, images, or macros.

## Choose an implementation

- Prefer Node.js for JavaScript/TypeScript automation and cross-platform agent scripts.
- Prefer C# for .NET applications and .NET Framework compatibility.
- Prefer Rust for native Rust applications or a small native CLI.
- Use the GitHub Pages lab when the workbook must stay in the browser and the user wants to compare C# and Rust output.

All maintained native implementations produce the same CRLF UTF-8 Markdown shape.

## Convert with Node.js

From a MiniMarkdown checkout:

```powershell
npm ci --prefix node
npm run build --prefix node
node node/dist/cli.js input.xlsx -o output.md
```

For standard input or standard output:

```powershell
node node/dist/cli.js input.xlsx
Get-Content input.xlsx -AsByteStream | node node/dist/cli.js -
```

TypeScript API:

```typescript
import { convertFile, convertStream } from "minimarkdown";

await convertFile("input.xlsx", "output.md");
await convertStream(input, output);
```

## Convert with C# or Rust

```powershell
dotnet build csharp/MiniMarkdown.sln -c Release
./csharp/src/MiniMarkdown.Cli/bin/Release/net462/minimarkdown.exe input.xlsx -o output.md
```

```powershell
cargo run --manifest-path rust/Cargo.toml --release -- input.xlsx -o output.md
```

## Apply the output contract

- Each non-empty worksheet becomes a level-two heading followed by one Markdown table.
- The first effective row is the table header.
- Empty outer rows and columns are removed; internal gaps remain.
- Pipes and backslashes are escaped; cell line breaks become `<br>`.
- Cached formula values are emitted, but formulas are never calculated.
- Dates and times use invariant ISO-like forms.

## Work safely

- Keep the default package, ZIP-entry, compression-ratio, row, and column limits unless the user explicitly requires a reviewed change.
- Do not upload workbooks to external services without user approval.
- For untrusted files, retain malformed ZIP, path traversal, external relationship, DTD, and resource-limit rejection.
- Run `./scripts/Validate-All.ps1` after implementation or behavior changes.
- For output, memory, or performance changes, also run the repository benchmark described in `ARCHITECTURE.md`.