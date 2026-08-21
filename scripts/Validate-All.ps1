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
    $runningOnWindows = [Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT
    if ($runningOnWindows -or (Get-Command mono -ErrorAction SilentlyContinue)) {
        Invoke-Checked 'Test C# integration behavior (Release)' {
            dotnet run --project csharp/tests/MiniMarkdown.Tests -c Release --no-build
        }
    }
    else {
        Write-Warning 'Skipping C# integration tests because Mono is not installed.'
    }
    Invoke-Checked 'Check Rust formatting' {
        cargo fmt --manifest-path rust/Cargo.toml -- --check
    }
    Invoke-Checked 'Test Rust (Release)' {
        cargo test --manifest-path rust/Cargo.toml --release
    }
    Invoke-Checked 'Test Node.js and TypeScript' {
        npm test --prefix node
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
    if ((Get-Command npx -ErrorAction SilentlyContinue) -and ((node --version) -match '^v(2[2-9]|[3-9][0-9])\.')) {
        Invoke-Checked 'Discover Agent Skills' {
            npx --yes skills add . --list
        }
    }
    else {
        Write-Warning 'Skipping skills.sh discovery because Node.js 22 or newer is not installed.'
    }
}
finally {
    Pop-Location
}

Write-Host 'All MiniMarkdown implementations passed validation.'