[CmdletBinding()]
param(
    [string] $OutputPath,
    [switch] $SkipAot
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
if (-not $OutputPath) {
    $OutputPath = Join-Path $repositoryRoot 'artifacts/site'
}

$sitePath = [System.IO.Path]::GetFullPath($OutputPath)
$publishPath = Join-Path $repositoryRoot 'artifacts/csharp-web'
$webSourcePath = Join-Path $repositoryRoot 'web'
$csharpProject = Join-Path $repositoryRoot 'csharp/src/MiniMarkdown.WebAssembly/MiniMarkdown.WebAssembly.csproj'
$rustManifest = Join-Path $repositoryRoot 'rust/Cargo.toml'

function Invoke-Checked {
    param(
        [Parameter(Mandatory)]
        [string] $Name,

        [Parameter(Mandatory)]
        [scriptblock] $Command
    )

    Write-Host "==> $Name"
    & $Command
    if ($LASTEXITCODE -ne 0) {
        throw "$Name failed with exit code $LASTEXITCODE."
    }
}

if (-not (Get-Command wasm-pack -ErrorAction SilentlyContinue)) {
    throw 'wasm-pack is required. Install it with: cargo install wasm-pack --locked'
}

$installedTargets = rustup target list --installed
if ($installedTargets -notcontains 'wasm32-unknown-unknown') {
    throw 'The Rust WebAssembly target is required. Install it with: rustup target add wasm32-unknown-unknown'
}

Remove-Item $sitePath -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item $publishPath -Recurse -Force -ErrorAction SilentlyContinue
New-Item $sitePath -ItemType Directory -Force | Out-Null

$runAot = if ($SkipAot) { 'false' } else { 'true' }
Invoke-Checked "Publish C# WebAssembly (AOT: $runAot)" {
    dotnet publish $csharpProject -c Release -r browser-wasm -o $publishPath -p:RunAOTCompilation=$runAot
}

$dotnetRuntime = Get-ChildItem $publishPath -Recurse -Filter 'dotnet.js' |
    Where-Object { $_.Directory.Name -eq '_framework' } |
    Select-Object -First 1
if (-not $dotnetRuntime) {
    throw "The C# WebAssembly publish did not produce _framework/dotnet.js under $publishPath."
}

$appBundlePath = Split-Path -Parent $dotnetRuntime.Directory.FullName
Copy-Item (Join-Path $appBundlePath '*') $sitePath -Recurse -Force
Copy-Item (Join-Path $webSourcePath '*') $sitePath -Recurse -Force

$rustOutputPath = Join-Path $sitePath 'rust'
Invoke-Checked 'Build Rust WebAssembly' {
    wasm-pack build (Split-Path -Parent $rustManifest) --target web --release --out-dir $rustOutputPath --out-name minimarkdown --no-pack
}

if (-not (Test-Path (Join-Path $rustOutputPath 'minimarkdown_bg.wasm'))) {
    throw 'The Rust WebAssembly build did not produce minimarkdown_bg.wasm.'
}

New-Item (Join-Path $sitePath '.nojekyll') -ItemType File -Force | Out-Null
Write-Host "Web test site: $sitePath"