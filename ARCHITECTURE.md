# MiniMarkdown Cross-Language Architecture

This document is the repository-level contract for every MiniMarkdown implementation. Language-specific APIs may be idiomatic, but observable conversion behavior and validation requirements are shared.

## Repository layout

- `csharp/` contains the C# library, CLI, integration tests, and benchmarks.
- `rust/` contains the Rust library, CLI, and integration tests.
- `web/` contains the shared static browser interface.
- `scripts/Validate-All.ps1` is the required cross-language validation entry point.
- `scripts/Build-Web.ps1` builds the C# AOT and Rust WebAssembly GitHub Pages artifact.
- `scripts/Run-Benchmark.ps1` runs the isolated XLSX performance and semantic benchmark.

Each language directory owns its build metadata and implementation details. Shared policy belongs at the repository root.

## Authoritative behavior

MiniMarkdown converts XLSX workbooks into deterministic GitHub-Flavored Markdown. Every implementation MUST preserve these behaviors:

1. Emit each non-empty worksheet in workbook order with an escaped level-two heading.
2. Use the first effective row as the table header and emit one separator row.
3. Trim empty outer rows and columns while preserving internal sparse rows and cells.
4. Escape backslashes and pipes in cells, and replace CRLF, CR, or LF cell breaks with `<br>`.
5. Emit shared strings, inline strings, booleans, numbers, errors, and cached formula values consistently.
6. Format recognized dates, times, date-times, and durations using invariant ISO-like forms.
7. Emit CRLF Markdown line endings on every platform. UTF-8 output MUST have no byte-order mark.
8. Skip empty worksheets and place one blank line between emitted worksheets.

The exact-output integration tests are authoritative. Reference converters such as anydoc and MarkItDown are semantic comparison inputs, not output authorities.

## Resource and security contract

Every implementation MUST:

- Parse worksheets incrementally and MUST NOT load an entire workbook, worksheet, or Markdown document into memory.
- Use two worksheet passes when effective bounds must be known before output.
- Keep caller-owned input and output resources open.
- Support non-seekable input with a bounded temporary package file.
- Keep shared-string storage bounded through disk-backed or equivalently bounded storage.
- Enforce positive limits for package bytes, uncompressed bytes, ZIP entries, compression ratio, rows, and columns.
- Reject external workbook relationships and package paths that escape the archive root.
- Avoid formula evaluation, macros, external resource loading, and XML DTD/entity expansion.

Default limits MUST remain behaviorally equivalent across implementations:

| Limit | Default |
| --- | ---: |
| Columns | 16,384 |
| Rows | 1,048,576 |
| Compressed package | 256 MiB |
| Total uncompressed ZIP data | 512 MiB |
| ZIP entries | 10,000 |
| Entry compression ratio | 1,000 |

The browser demo is an intentionally constrained deployment surface. It accepts packages up to 16 MiB, processes files locally, and may use in-memory package and shared-string storage because browser WebAssembly has no portable temporary-file API. Native library and CLI implementations remain subject to the disk-backed bounded-memory contract.

## WebAssembly contract

The GitHub Pages test site MUST expose both maintained implementations:

- C# is published with .NET WebAssembly AOT and exported through `JSExport`.
- Rust is compiled to `wasm32-unknown-unknown` and packaged with `wasm-bindgen`/`wasm-pack`.
- Both engines consume the same uploaded XLSX bytes and return the authoritative Markdown shape.
- Compare mode reports whether outputs are byte-identical and allows either output to be inspected.
- Workbook bytes MUST remain local to the browser. The static site MUST NOT upload workbook content.

The Pages artifact is assembled only through `scripts/Build-Web.ps1`. GitHub Actions MUST call this script rather than duplicating build behavior in workflow YAML.

## CLI contract

All CLIs MUST support:

```text
minimarkdown <input.xlsx|-> [-o output.md]
```

- `-` reads XLSX bytes from standard input.
- Without `-o`, Markdown is written to standard output.
- `-h` and `--help` print usage and return success.
- Missing arguments or a missing `-o` value return exit code `2`.
- Conversion failures use exit code `1` and prefix diagnostics with `Conversion failed:`.

## Validation gates

Run the full repository gate before merging:

```powershell
.\scripts\Validate-All.ps1
```

The gate MUST build and test every maintained implementation. Each implementation's integration suite MUST cover at least:

- Exact common-cell and sparse-table output.
- Caller-owned and non-seekable input behavior.
- Malformed input and resource-limit rejection.
- A large worksheet with incremental output evidence.

Changes to output shape, parsing, memory behavior, or performance MUST also run the XLSX benchmark. Performance conclusions require at least one warmup and three measured iterations; a single iteration is only a smoke test.

Changes to `web/`, either WebAssembly export, or the site build pipeline MUST run:

```powershell
.\scripts\Build-Web.ps1
```

Use `-SkipAot` only for local integration checks. GitHub Pages deployment MUST use the default AOT build.

## Adding another language

A new implementation is complete only when it:

1. Lives in a root-level language directory.
2. Provides a library API and the shared CLI contract.
3. Implements all authoritative and resource behaviors above.
4. Includes dependency-light integration tests for the required matrix.
5. Is added to `scripts/Validate-All.ps1`.
6. Produces byte-identical Markdown for shared exact-output cases.

Behavior changes require updating this document and equivalent tests in every maintained language in the same change.