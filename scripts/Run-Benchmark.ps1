<#
.SYNOPSIS
    Generate XLSX cases, benchmark MiniMarkdown against reference tools, and write reports.

.EXAMPLE
    .\scripts\Run-Benchmark.ps1
    .\scripts\Run-Benchmark.ps1 -Tools minimarkdown -Iterations 1
    .\scripts\Run-Benchmark.ps1 -Filter tall -SkipGenerate
#>

param(
    [string]$Tools = "minimarkdown,anydoc,markitdown",
    [int]$Iterations = 3,
    [int]$Warmups = 1,
    [string]$Filter,
    [switch]$SkipGenerate,
    [string]$CorpusDir,
    [string]$ReportDir,
    [string]$AnydocCommand,
    [string]$MarkItDownCommand
)

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
$Project = Join-Path $Root "csharp\benchmarks\MiniMarkdown.Benchmarks\MiniMarkdown.Benchmarks.csproj"

if (-not $CorpusDir) { $CorpusDir = Join-Path $Root "csharp\benchmarks\artifacts\corpus" }
if (-not $ReportDir) { $ReportDir = Join-Path $Root "csharp\benchmarks\artifacts\reports" }

if (-not $AnydocCommand) {
    $npx = Get-Command npx.cmd -ErrorAction SilentlyContinue
    $AnydocCommand = if ($npx) { '"' + $npx.Source + '" --yes @firecrawl/anydoc' } else { "anydoc" }
}

if (-not $MarkItDownCommand) {
    $uv = Get-Command uv.exe -ErrorAction SilentlyContinue
    $markItDownProject = Join-Path $Root "..\markitdown\packages\markitdown"
    $MarkItDownCommand = if ($uv -and (Test-Path $markItDownProject)) {
        '"' + $uv.Source + '" run --project "' + (Resolve-Path $markItDownProject).Path + '" --extra xlsx markitdown'
    } else {
        "markitdown"
    }
}

$arguments = @(
    "run",
    "--corpus", $CorpusDir,
    "--reports", $ReportDir,
    "--tools", $Tools,
    "--iterations", $Iterations,
    "--warmups", $Warmups,
    "--anydoc", $AnydocCommand,
    "--markitdown", $MarkItDownCommand
)
if ($Filter) { $arguments += @("--filter", $Filter) }
if ($SkipGenerate) { $arguments += "--skip-generate" }

Write-Host "MiniMarkdown XLSX Benchmark" -ForegroundColor Cyan
Write-Host "Corpus: $CorpusDir"
Write-Host "Reports: $ReportDir"
Write-Host "Tools: $Tools"

& dotnet run --project $Project -c Release -- @arguments
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$report = Join-Path $ReportDir "benchmark_report.md"
Write-Host "Report: $report" -ForegroundColor Green