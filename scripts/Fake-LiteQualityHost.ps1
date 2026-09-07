param([string]$Mode = 'Good')
$ErrorActionPreference = 'Stop'
[Console]::InputEncoding = [Text.UTF8Encoding]::new($false)
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
$calls = 0
while ($null -ne ($line = [Console]::ReadLine())) {
    $request = $line | ConvertFrom-Json
    if ($request.command -eq 'shutdown') { break }
    $result = @{ calls = $calls }
    if ($request.command -eq 'translate') {
        $calls++
        $result = @{ translations = @('섬광 쓸 때까지 기다려') }
        switch ($Mode) {
            'Broken' { $result.translations = @('??') }
            'Negative' { $result.translations = @('오른쪽으로 가') }
            'WrongCount' { $result.translations = @('하나','둘') }
            'WrongType' { $result.translations = @(42) }
        }
    }
    [Console]::WriteLine((@{id=$request.id;ok=$true;result=$result} | ConvertTo-Json -Depth 5 -Compress))
}
