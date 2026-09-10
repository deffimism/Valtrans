param(
    [ValidateSet('Unit', 'Smoke', 'Quick', 'Baseline', 'ZhSmoke', 'Fuzz', 'Full')]
    [string]$Profile = 'Smoke'
)
$ErrorActionPreference = 'Stop'
$root = Resolve-Path "$PSScriptRoot/.."
. "$PSScriptRoot/Resolve-ValtransBuild.ps1"
switch ($Profile) {
    'Unit' {
        dotnet test "$root/test/Valtrans.Tests/Valtrans.Tests.csproj" -c Release
    }
    'Smoke' {
        & "$PSScriptRoot/Test-MessageTrace.ps1"
        & "$PSScriptRoot/Run-E2ESmoke.ps1" -NoFocusArena
    }
    'Quick' {
        & "$PSScriptRoot/Test-LocalFirst.ps1"
        & "$PSScriptRoot/Run-E2ESmoke.ps1" -NoFocusArena
    }
    'Baseline' {
        & "$PSScriptRoot/Run-AllBaselines.ps1"
    }
    'ZhSmoke' {
        & "$PSScriptRoot/Test-MessageTrace.ps1"
        & "$PSScriptRoot/Run-E2EZhSmoke.ps1" -NoFocusArena
    }
    'Fuzz' {
        & "$PSScriptRoot/Run-ArenaFuzz.ps1"
    }
    'Full' {
        dotnet test "$root/test/Valtrans.Tests/Valtrans.Tests.csproj" -c Release
        & "$PSScriptRoot/Test-MessageTrace.ps1"
        & "$PSScriptRoot/Run-E2ESmoke.ps1" -NoFocusArena
        & "$PSScriptRoot/Run-ArenaFuzz.ps1"
        $fastMarker = Join-Path $env:LOCALAPPDATA 'Valtrans/FastOcrRuntime/fast_model.json'
        if (Test-Path -LiteralPath $fastMarker) {
            & "$PSScriptRoot/Run-E2EZhSmoke.ps1" -NoFocusArena
            & "$PSScriptRoot/Run-E2EHybridSmoke.ps1" -NoFocusArena
        } else {
            Write-Output 'SKIP: ZhSmoke (Fast OCR runtime missing)'
        }
        $baseline = Join-Path $root 'artifacts/baseline-v0.2.3-windows.json'
        if (Test-Path -LiteralPath $baseline) {
            & "$PSScriptRoot/Compare-Baseline.ps1" -BaselinePath $baseline -CurrentPath $baseline
            Write-Output 'Baseline compare tooling: PASS (windows self-check)'
        } else {
            Write-Output 'SKIP: baseline compare (artifacts/baseline-v0.2.3-windows.json missing)'
        }
        $zhBaseline = Join-Path $root 'artifacts/baseline-v0.3.0-zh-fast.json'
        if (Test-Path -LiteralPath $zhBaseline) {
            & "$PSScriptRoot/Compare-Baseline.ps1" -BaselinePath $zhBaseline -CurrentPath $zhBaseline
            Write-Output 'Baseline compare tooling: PASS (zh-fast self-check)'
        } elseif (Test-Path -LiteralPath $fastMarker) {
            Write-Output 'SKIP: zh-fast baseline compare (artifacts/baseline-v0.3.0-zh-fast.json missing)'
        }
    }
}
Write-Output "test.ps1 [$Profile]: PASS"
