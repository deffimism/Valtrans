param([Parameter(Mandatory)][string]$ResultDirectory, [string[]]$Profiles=@('hybrid18','hybrid7','lite'))
$ErrorActionPreference='Stop'
$rows=@()
$order=@('send-en','send-jp','receive-en','receive-jp')
foreach($profile in $Profiles){
    $grades=@{}
    Import-Csv "$ResultDirectory/grades-$profile.tsv" -Delimiter "`t"|ForEach-Object{$grades[$_.family]=$_}
    foreach($result in (Get-Content "$ResultDirectory/$profile.jsonl"|ForEach-Object{$_|ConvertFrom-Json})){
        $family=($result.id -split '-',2)[0]
        $direction=($result.id -split '-',2)[1]
        $grade=[string]$grades[$family].grades[$order.IndexOf($direction)]
        if($grade -notin @('A','W','E','X')){throw "Missing grade $profile/$($result.id)"}
        if(($grade -eq 'X') -ne [bool]$result.error){throw "Error/grade mismatch $profile/$($result.id)"}
        $rows += [pscustomobject]@{profile=$profile;id=$result.id;direction=$direction;category=$result.category;grade=$grade;source=$result.source;target=$result.target;reference=$result.reference;output=$result.output;error=$result.error;ms=$result.ms;route=$result.route;validator=$result.validator;notes=$grades[$family].notes}
    }
}
$rows|Export-Csv "$ResultDirectory/reviewed-results.csv" -NoTypeInformation -Encoding utf8BOM
function Measure-Quality($group){
    if($group.Count -eq 0){return @{n=0}}
    $times=@($group.ms|Sort-Object)
    [ordered]@{n=$group.Count;A=@($group|Where-Object grade -eq A).Count;W=@($group|Where-Object grade -eq W).Count;E=@($group|Where-Object grade -eq E).Count;X=@($group|Where-Object grade -eq X).Count;p50=$times[[int][Math]::Ceiling($times.Count*0.5)-1];p95=$times[[int][Math]::Ceiling($times.Count*0.95)-1];max=$times[-1]}
}
$summary=[ordered]@{profiles=@{};directions=@{};categories=@{};paired=@{};validation=@{}}
$comparisonIds=@($rows|Where-Object profile -eq hybrid7|Select-Object -ExpandProperty id)
foreach($profile in $Profiles){
    $all=@($rows|Where-Object profile -eq $profile)
    $summary.profiles[$profile]=Measure-Quality $all
    $summary.paired[$profile]=Measure-Quality @($all|Where-Object id -In $comparisonIds)
    $summary.validation[$profile]=@{errorButPass=@($all|Where-Object {$_.grade -eq 'E' -and $_.validator -eq 'PASS'}).Count;usableButFail=@($all|Where-Object {$_.grade -in @('A','W') -and $_.validator -ne 'PASS'}).Count}
    foreach($direction in $order){$summary.directions["$profile/$direction"]=Measure-Quality @($all|Where-Object direction -eq $direction)}
    foreach($category in @($all.category | Sort-Object -Unique)){ $summary.categories["$profile/$category"]=Measure-Quality @($all|Where-Object category -eq $category)}
}
$summary|ConvertTo-Json -Depth 8|Set-Content "$ResultDirectory/summary.json" -Encoding utf8
$markdown=[Text.StringBuilder]::new()
[void]$markdown.AppendLine('# 전체 번역 결과와 검토')
[void]$markdown.AppendLine('A=의미 보존·사용 가능, W=이해 가능하나 어색/모호, E=의미 오류, X=예외·차단. 이 판정은 에이전트의 원문 대조 검토이며 앱의 validator PASS와 다릅니다. 동일 의미의 다른 표현도 허용했습니다. notes는 같은 상황 4방향의 종합 메모입니다.')
foreach($profile in $Profiles){
    [void]$markdown.AppendLine("`n## $profile`n")
    [void]$markdown.AppendLine('| ID | 판정 | 원문 | 실제 출력 또는 예외 | 내부 검사 | 메모 |')
    [void]$markdown.AppendLine('|---|---|---|---|---|---|')
    foreach($row in ($rows|Where-Object profile -eq $profile)){
        $output=if($row.error){$row.error}else{$row.output}
        $cells=@($row.id,$row.grade,$row.source,$output,$row.validator,$row.notes)|ForEach-Object{([string]$_).Replace('|','\|').Replace("`n",'<br>').Replace("`r",'')}
        [void]$markdown.AppendLine('| '+($cells -join ' | ')+' |')
    }
}
[IO.File]::WriteAllText("$ResultDirectory/reviewed-results.md",$markdown.ToString(),[Text.UTF8Encoding]::new($false))
$summary|ConvertTo-Json -Depth 8
