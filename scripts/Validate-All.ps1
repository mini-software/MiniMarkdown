[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot

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

Push-Location $repositoryRoot
try {
    Invoke-Checked 'Build C# (Release)' {
        dotnet build csharp/MiniMarkdown.sln -c Release
    }
    Invoke-Checked 'Test C# integration behavior (Release)' {
        dotnet run --project csharp/tests/MiniMarkdown.Tests -c Release --no-build
    }
    Invoke-Checked 'Check Rust formatting' {
        cargo fmt --manifest-path rust/Cargo.toml -- --check
    }
    Invoke-Checked 'Test Rust (Release)' {
        cargo test --manifest-path rust/Cargo.toml --release
    }
    $installedWorkloads = dotnet workload list
    if ($installedWorkloads -match 'wasm-tools') {
        Invoke-Checked 'Build C# WebAssembly host' {
            dotnet build csharp/src/MiniMarkdown.WebAssembly/MiniMarkdown.WebAssembly.csproj -c Release -p:RunAOTCompilation=false
        }
    }
    else {
        Write-Warning 'Skipping the C# WebAssembly build because wasm-tools is not installed.'
    }
    if ((rustup target list --installed) -contains 'wasm32-unknown-unknown') {
        Invoke-Checked 'Build Rust WebAssembly library' {
            cargo build --manifest-path rust/Cargo.toml --lib --release --target wasm32-unknown-unknown
        }
    }
    else {
        Write-Warning 'Skipping Rust WebAssembly build because wasm32-unknown-unknown is not installed.'
    }
}
finally {
    Pop-Location
}

Write-Host 'All MiniMarkdown implementations passed validation.'