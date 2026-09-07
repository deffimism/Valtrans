# Isolated protocol fixture. Never loads models or accesses real chat/settings.
$ErrorActionPreference = 'Stop'
[Console]::InputEncoding = [Text.UTF8Encoding]::new($false)
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
while ($null -ne ($line = [Console]::ReadLine())) {
    $request = $line | ConvertFrom-Json
    if ($request.command -eq 'shutdown') { break }
    if ($request.command -eq 'slow') { Start-Sleep -Milliseconds 350 }
    if ($request.command -eq 'stale') {
        [Console]::WriteLine('{"id":"old-request","ok":true,"result":{"text":"WRONG"}}')
    }
    if ($request.command -eq 'badid') {
        [Console]::WriteLine('{"ok":true,"result":{"text":"WRONG"}}')
        continue
    }
    [Console]::WriteLine((@{ id = $request.id; ok = $true; result = @{ text = $request.command } } | ConvertTo-Json -Compress -Depth 4))
}
