param([string]$AssemblyPath = "$PSScriptRoot/../bin/Release/net10.0-windows10.0.26100.0/Valtrans.dll")
$ErrorActionPreference = 'Stop'
$assembly = (Resolve-Path -LiteralPath $AssemblyPath).Path
Add-Type -Path $assembly
$referencePack = Get-ChildItem 'C:/Program Files/dotnet/packs/Microsoft.NETCore.App.Ref' -Directory |
    Where-Object Name -Like '10.*' | Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
$references = @((Get-ChildItem (Join-Path $referencePack.FullName 'ref/net10.0/*.dll')).FullName) + @($assembly)
Add-Type -ReferencedAssemblies ($references | Select-Object -Unique) -TypeDefinition @'
using System;
using System.Linq;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Valtrans.Services;
public static class OcrPipelineChecks {
    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    static OcrPositionedLine Row(string text, int y, int x=0) => new(text,x,y,400,20,Array.Empty<OcrPositionedWord>());
    public static async Task Run() {
        foreach (var label in new[]{"팀:", "팀: |", "팀: not yet sent", "오늘", "(방송) 나 (피닉스) 님이 장비를 요청합니다!"})
            Check(ChatTextSanitizer.NormalizeOcrBody(label)=="", "Input/system label retained: "+label);
        Check(ChatTextSanitizer.NormalizeOcrBody("(팀) 나: 오늘") == "오늘", "Real today message lost");
        Check(ChatTextSanitizer.NormalizeOcrBody("(팀) 나: no enemies left") == "no enemies left", "Negation lost");
        var frame = new OcrReadResult("", "MIXED", PositionedLines: new[]{
            Row("(시스템) 네온 님이 게임에서 나갔습니다. 이번 라운드에서 소환되지",0),
            Row("않습니다.",25), Row("(팀) 한글닉네임: ミッド2",70), Row("팀: |",110)});
        var bodies = OcrMessageParser.Extract(frame);
        Check(bodies.Count==1 && bodies[0].Body=="ミッド2", "System wrap/nickname filtering failed");
        var selected = OcrLineSelector.Select(new Dictionary<string,IReadOnlyList<OcrPositionedLine>> {
            ["KO"] = new[]{Row("(시스템) 네온 님의 연결이 끊어졌습니다.",0)},
            ["JP"] = new[]{Row("( 人 陸 ) 鬯 円 召 01 O-I 類 合 L 号 .",0)} });
        Check(selected.Text.StartsWith("(시스템)"), "Gibberish beat recognizable system label");

        var processed = new ConcurrentQueue<string>();
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using (var queue = new IncomingOcrQueue((line,token)=> {
            if (line=="bad") throw new InvalidOperationException();
            processed.Enqueue(line);
            if(line=="second") finished.TrySetResult();
            return Task.CompletedTask;
        }, _=>{}, CancellationToken.None)) {
            queue.Enqueue("bad\nfirst\nfirst\nsecond");
            await finished.Task.WaitAsync(TimeSpan.FromSeconds(4));
            Check(processed.SequenceEqual(new[]{"first","second"}), "Line failure/duplicate lost following chat");
        }
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var drained = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        processed.Clear();
        using (var queue = new IncomingOcrQueue(async (line,token)=> {
            if(line=="active") { entered.TrySetResult(); await release.Task.WaitAsync(token); }
            processed.Enqueue(line);
            if(line=="n19") drained.TrySetResult();
        }, _=>{}, CancellationToken.None)) {
            queue.Enqueue("active"); await entered.Task.WaitAsync(TimeSpan.FromSeconds(4));
            queue.Enqueue(string.Join("\n",Enumerable.Range(0,20).Select(i=>"n"+i)));
            release.TrySetResult(); await drained.Task.WaitAsync(TimeSpan.FromSeconds(4));
            Check(processed.Count==9 && !processed.Contains("n0") && processed.Contains("n12"), "Queue not bounded to latest 8");
        }
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var oldQueue = new IncomingOcrQueue(async (_,token)=> {
            started.TrySetResult();
            try { await Task.Delay(30000,token); }
            finally { cancelled.TrySetResult(); }
        }, _=>{}, CancellationToken.None);
        oldQueue.Enqueue("old"); await started.Task.WaitAsync(TimeSpan.FromSeconds(4));
        oldQueue.Dispose(); await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(4));
        await oldQueue.Completion.WaitAsync(TimeSpan.FromSeconds(4));
        oldQueue.Enqueue("after dispose");
    }
}
'@
[OcrPipelineChecks]::Run().GetAwaiter().GetResult()
$similar = [Valtrans.MainWindow].GetMethod('AreSimilarOcrText', [Reflection.BindingFlags]'Static,NonPublic')
$midTwo = -join (0x30DF, 0x30C3, 0x30C9, 0x4E8C, 0x4EBA, 0x3044, 0x308B | ForEach-Object { [char]$_ })
$midThree = -join (0x30DF, 0x30C3, 0x30C9, 0x4E09, 0x4EBA, 0x3044, 0x308B | ForEach-Object { [char]$_ })
foreach ($pair in @(@('pushmidnow','nopushmidnow'), @('twoleftmaybe','twoleft'), @($midTwo, $midThree))) {
    if ($similar.Invoke($null, $pair)) { throw "Important fact deduplicated: $pair" }
}
'PASS: system selection/wraps, nickname/input removal, negation/count dedup, line failure isolation, bounded queue, stop cancellation'
