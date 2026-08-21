# MiniMarkdown

[![Validate](https://github.com/shps951023/MiniMarkdown/actions/workflows/deploy-pages.yml/badge.svg)](https://github.com/shps951023/MiniMarkdown/actions/workflows/deploy-pages.yml)
[![GitHub Pages](https://img.shields.io/badge/try-GitHub%20Pages-171817)](https://shps951023.github.io/MiniMarkdown/)
[![skills.sh](https://skills.sh/b/shps951023/MiniMarkdown)](https://skills.sh/shps951023/MiniMarkdown)

MiniMarkdown converts XLSX workbooks into deterministic GitHub-Flavored Markdown. The repository maintains matching C#, Rust, and Node.js implementations with exact-output tests, bounded native conversion, command-line tools, and a browser comparison lab compiled to WebAssembly.

**[Try the C# AOT and Rust WebAssembly converters in your browser](https://shps951023.github.io/MiniMarkdown/)**

Files selected in the browser stay on the device. The demo sends no workbook data to a server.

## Implementations

| | C# | Rust | Node.js |
| --- | --- | --- | --- |
| Library | .NET Standard 2.0 / .NET Framework 4.6.2 | Rust crate | JavaScript + TypeScript declarations |
| CLI | .NET Framework 4.6.2 | Native binary | Node.js 20+ |
| Browser | .NET WebAssembly AOT | `wasm32-unknown-unknown` | Native only |
| Native shared strings | Temporary-file backed | Temporary-file backed | Temporary-file backed |
| Exact output | CRLF UTF-8 Markdown | CRLF UTF-8 Markdown | CRLF UTF-8 Markdown |

Both implementations follow [ARCHITECTURE.md](ARCHITECTURE.md), the authoritative cross-language behavior and resource contract.

## Output

Each non-empty worksheet becomes a Markdown section and table:

```markdown
## Data

| Name | Active | Date |
| --- | --- | --- |
| Alice | TRUE | 2023-03-15 |
```

MiniMarkdown preserves internal sparse rows and columns, escapes Markdown cell content, emits cached formula results, and formats recognized dates and times consistently. It does not evaluate formulas or process charts, images, macros, OCR, XLS, or XLSB.

## CLI

C#:

```powershell
dotnet build csharp/MiniMarkdown.sln -c Release
./csharp/src/MiniMarkdown.Cli/bin/Release/net462/minimarkdown.exe report.xlsx -o report.md
```

Rust:

```powershell
cargo run --manifest-path rust/Cargo.toml --release -- report.xlsx -o report.md
```

Node.js:

```powershell
npm ci --prefix node
npm run build --prefix node
node node/dist/cli.js report.xlsx -o report.md
```

Both CLIs write to standard output when `-o` is omitted and accept `-` for standard input.

## Libraries

C#:

```csharp
using (Stream input = File.OpenRead("report.xlsx"))
using (TextWriter output = File.CreateText("report.md"))
{
    new MiniMarkdown.XlsxConverter().Convert(input, output);
}
```

Rust:

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

TypeScript / JavaScript:

```typescript
import { convertFile, convertStream } from "minimarkdown";

await convertFile("report.xlsx", "report.md");
await convertStream(input, output);
```

Caller-owned streams remain open. Native non-seekable input is copied to a bounded temporary file because XLSX entries must be revisited.

## Validation

Run every maintained native implementation and WebAssembly compile check:

```powershell
./scripts/Validate-All.ps1
```

Build the complete browser test site:

```powershell
dotnet workload install wasm-tools
rustup target add wasm32-unknown-unknown
cargo install wasm-pack --locked
./scripts/Build-Web.ps1
```

The generated static site is written to `artifacts/site`. Use `./scripts/Build-Web.ps1 -SkipAot` for a faster local integration build; GitHub Pages always uses full C# AOT publishing and optimized Rust WebAssembly.

## GitHub Pages

[.github/workflows/deploy-pages.yml](.github/workflows/deploy-pages.yml) validates both native implementations, builds both browser engines, enables GitHub Pages, and deploys `artifacts/site` on pushes to `main` or manual dispatch. If repository or organization policy blocks automatic enablement, set **Pages > Build and deployment > Source** to **GitHub Actions** once.

Azure resources and Azure CLI are not required for GitHub Pages. The site is fully static and runs conversion in the browser.

## Agent Skill

Install the MiniMarkdown XLSX conversion skill for GitHub Copilot, Claude Code, Cursor, Codex, and other compatible agents:

```powershell
npx skills add shps951023/MiniMarkdown
```

The skill is defined in [skills/minimarkdown-xlsx/SKILL.md](skills/minimarkdown-xlsx/SKILL.md). After the repository is public and this change is pushed, the first installation registers anonymous telemetry and makes the skill eligible to appear at [skills.sh/shps951023/MiniMarkdown](https://skills.sh/shps951023/MiniMarkdown). skills.sh hosts the skill catalog; the Node.js package itself remains sourced from this repository.

## Benchmarks

Run a quick MiniMarkdown smoke test:

```powershell
./scripts/Run-Benchmark.ps1 -Tools minimarkdown -Iterations 1 -Warmups 0 -Filter basic
```

Run the full isolated-process comparison with MiniMarkdown, anydoc, and MarkItDown:

```powershell
./scripts/Run-Benchmark.ps1
```

See [csharp/benchmarks/README.md](csharp/benchmarks/README.md) for corpus, metrics, and reference-tool setup. MiniMarkdown exact-output tests are authoritative; reference converters are compared semantically.