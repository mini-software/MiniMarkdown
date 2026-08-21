# MiniMarkdown for Rust

The Rust implementation converts XLSX workbooks to the same deterministic GitHub-Flavored Markdown table shape as the C# implementation. It performs two streaming worksheet passes, stores shared strings in a temporary file, and applies bounded ZIP resource limits.

## CLI

```powershell
cargo run --manifest-path rust/Cargo.toml -- report.xlsx -o report.md
cargo run --manifest-path rust/Cargo.toml -- report.xlsx
Get-Content report.xlsx -AsByteStream | cargo run --manifest-path rust/Cargo.toml -- -
```

## Library

```rust
use minimarkdown::{ConversionOptions, XlsxConverter};

let mut input = std::fs::File::open("report.xlsx")?;
let mut output = std::fs::File::create("report.md")?;
XlsxConverter::convert_seekable(
    &mut input,
    &mut output,
    &ConversionOptions::default(),
)?;
```

Use `XlsxConverter::convert` for non-seekable readers. It copies the bounded package to a temporary file while leaving caller-owned readers and writers open.

## Validation

```powershell
cargo test --manifest-path rust/Cargo.toml
```