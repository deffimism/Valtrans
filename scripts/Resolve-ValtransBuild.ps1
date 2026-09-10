function Get-ValtransBuildOutput {
    param(
        [Parameter(Mandatory)][string]$Root,
        [Parameter(Mandatory)][string]$ProjectRelativePath,
        [Parameter(Mandatory)][string]$FileName
    )
    $candidates = @(
        (Join-Path $Root "$ProjectRelativePath/bin/Release/net10.0-windows10.0.26100.0/$FileName"),
        (Join-Path $Root "$ProjectRelativePath/bin/Release/net10.0/$FileName")
    )
    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate) {
            return (Resolve-Path -LiteralPath $candidate).Path
        }
    }
    throw "Build output missing: $FileName ($ProjectRelativePath). Run: dotnet build -c Release"
}

function Get-ValtransTestRunner {
    param([string]$Root = (Resolve-Path "$PSScriptRoot/..").Path)
    Get-ValtransBuildOutput -Root $Root -ProjectRelativePath 'test/TestRunner' -FileName 'Valtrans.TestRunner.exe'
}

function Get-ValtransTestArena {
    param([string]$Root = (Resolve-Path "$PSScriptRoot/..").Path)
    Get-ValtransBuildOutput -Root $Root -ProjectRelativePath 'test/TestArena' -FileName 'Valtrans.TestArena.exe'
}

function Get-ValtransAppDll {
    param([string]$Root = (Resolve-Path "$PSScriptRoot/..").Path)
    Get-ValtransBuildOutput -Root $Root -ProjectRelativePath '.' -FileName 'Valtrans.dll'
}

function Stop-ValtransE2EProcesses {
    Get-Process -Name 'Valtrans.TestArena' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Get-CimInstance Win32_Process -Filter "Name='Valtrans.exe'" -ErrorAction SilentlyContinue |
        Where-Object { $_.CommandLine -like '*--test-mode*' } |
        ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
    Start-Sleep -Milliseconds 500
}
