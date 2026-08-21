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
}
finally {
    Pop-Location
}

Write-Host 'All MiniMarkdown implementations passed validation.'