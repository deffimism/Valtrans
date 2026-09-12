param(
    [Parameter(Mandatory)][string]$BaselineDirectory,
    [Parameter(Mandatory)][string]$ResultDirectory,
    [Parameter(Mandatory)][ValidateSet('hybrid18','hybrid7','lite')][string]$Profile
)
$ErrorActionPreference='Stop'
$before=@{}
Get-Content "$BaselineDirectory/$Profile.jsonl" | ForEach-Object { $r=$_|ConvertFrom-Json; $before[$r.id]=$r }
$baselineGrades=@{}
Import-Csv "$BaselineDirectory/grades-$Profile.tsv" -Delimiter "`t" | ForEach-Object { $baselineGrades[$_.family]=$_ }
$review=@{}
Import-Csv "$ResultDirectory/review-delta-$Profile.tsv" -Delimiter "`t" | ForEach-Object {
    if($review.ContainsKey($_.id)) { throw "Duplicate review: $($_.id)" }
    $review[$_.id]=$_
}
$order=@('send-en','send-jp','receive-en','receive-jp')
$families=[ordered]@{}
$used=[Collections.Generic.HashSet[string]]::new()
foreach($line in Get-Content "$ResultDirectory/$Profile.jsonl") {
    $r=$line|ConvertFrom-Json
    $b=$before[$r.id]
    if(!$b -or $r.source -cne $b.source -or $r.target -cne $b.target) { throw "Source changed: $($r.id)" }
    $family,$direction=$r.id -split '-',2
    $position=$order.IndexOf($direction)
    if($position -lt 0) {throw "Invalid direction $direction"}
    if($r.output -cne $b.output -or $r.error -cne $b.error) {
        if(!$review.ContainsKey($r.id)) { throw "Changed output not reviewed: $($r.id)" }
        $g=$review[$r.id].grade
        $note="$direction`: $($review[$r.id].note)"
        [void]$used.Add($r.id)
    } else {
        $g=[string]$baselineGrades[$family].grades[$position]
        $note="$direction`: unchanged output/error; inherited $BaselineDirectory grade"
    }
    if($g -notin @('A','W','E','X') -or (($g -eq 'X') -ne [bool]$r.error)) { throw "Invalid grade: $($r.id)" }
    if(!$families.Contains($family)) { $families[$family]=@{grades=[string[]]::new(4);notes=[Collections.Generic.List[string]]::new()} }
    if($families[$family].grades[$position]) {throw "Duplicate result $($r.id)"}
    $families[$family].grades[$position]=$g
    $families[$family].notes.Add($note)
}
if($used.Count -ne $review.Count) {throw 'Review contains unchanged or absent results'}
$rows=foreach($key in $families.Keys) {
    $f=$families[$key]
    if(@($f.grades | Where-Object {!$_}).Count) {throw "Incomplete family $key"}
    [pscustomobject]@{family=$key;grades=$f.grades -join '';notes=$f.notes -join '; '}
}
$rows | Export-Csv "$ResultDirectory/grades-$Profile.tsv" -Delimiter "`t" -NoTypeInformation -Encoding utf8
Write-Output "Reviewed changes: $($used.Count); total families: $($rows.Count)"
