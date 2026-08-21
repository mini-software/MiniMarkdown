# XLSX Benchmarks

The benchmark pipeline follows MiniPdf's generate, convert, compare, and report workflow while remaining dependency-free for MiniMarkdown itself.

## Cases

| Case | Workload |
| --- | --- |
| `01_basic_mixed_100x5` | Small mixed-type baseline |
| `02_multi_sheet_4x2500` | Four worksheets and 10,000 data rows |
| `03_wide_200x100` | Wide table with 100 columns |
| `04_sparse_10000_rows` | Sparse cells and internal row gaps |
| `05_shared_strings_50000x5` | 250,000 shared-string cells |
| `06_long_text_5000x4` | Long text and Markdown escaping |
| `07_dates_formulas_10000x5` | Dates, durations, cached formulas, and errors |
| `08_tall_100000x5` | 100,000-row streaming workload |

## Run

```powershell
.\scripts\Run-Benchmark.ps1
.\scripts\Run-Benchmark.ps1 -Tools minimarkdown -Iterations 1
.\scripts\Run-Benchmark.ps1 -Filter shared_strings -SkipGenerate
```

The runner creates XLSX files under `csharp/benchmarks/artifacts/corpus` and writes `benchmark_report.md` plus `benchmark_report.json` under `csharp/benchmarks/artifacts/reports`.

Each measured conversion runs in a fresh process. The report includes median and minimum wall-clock time, sampled peak working set for the full process tree, output size, and Markdown semantic shape. MiniMarkdown output is the semantic baseline; anydoc and MarkItDown are references, not byte-for-byte authorities.

The PowerShell entry point uses `npx @firecrawl/anydoc` and the sibling `D:\git\markitdown` checkout through `uv` when available. Override either command with `-AnydocCommand` or `-MarkItDownCommand`.

For stable performance comparisons, build in Release, close unrelated heavy processes, use at least one warmup and three measured iterations, and compare reports from the same machine.