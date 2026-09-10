param(
    [int]$Count = 10,
    [string]$TraceId = "",
    [string]$LogPath = ""
)
$ErrorActionPreference = 'Stop'
Add-Type -Path (Resolve-Path "$PSScriptRoot/../bin/Release/net10.0-windows10.0.26100.0/Valtrans.dll")
if ([string]::IsNullOrWhiteSpace($LogPath)) {
    $records = [Valtrans.Services.MessageTraceService]::ReadFromLog($null, $Count)
} else {
    $records = [Valtrans.Services.MessageTraceService]::ReadFromLog($LogPath, $Count)
}
if ($TraceId) {
    $records = @($records | Where-Object { $_.Id -eq $TraceId })
}
$records | ConvertTo-Json -Depth 8
