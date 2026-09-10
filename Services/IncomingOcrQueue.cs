namespace Valtrans.Services;

// One worker per OCR session. A failed/slow line cannot discard the rest of a batch.
// Cancellation and queue ownership never cross a stop/start boundary.
public sealed class IncomingOcrQueue : IDisposable
{
    private readonly object _gate = new();
    private readonly Queue<(string Text, DateTime Enqueued)> _pending = new();
    private readonly SemaphoreSlim _signal = new(0);
    private readonly CancellationTokenSource _stop;
    private readonly Func<string, CancellationToken, Task> _process;
    private readonly Action<string> _notice;
    private readonly Task _worker;
    private string? _active;
    private bool _disposed;
    public Task Completion => _worker;

    public IncomingOcrQueue(Func<string, CancellationToken, Task> process, Action<string> notice,
        CancellationToken cancellationToken)
    {
        _process = process;
        _notice = notice;
        _stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _worker = Task.Run(RunAsync);
    }

    public void Enqueue(string text)
    {
        lock (_gate)
        {
            if (_disposed || _stop.IsCancellationRequested) return;
            foreach (var line in text.Split(new[] { '\r', '\n' }, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                if (line.Equals(_active, StringComparison.OrdinalIgnoreCase) ||
                    _pending.Any(item => item.Text.Equals(line, StringComparison.OrdinalIgnoreCase))) continue;
                if (_pending.Count >= 2)
                {
                    _pending.Dequeue();
                    _notice("최신 메시지 우선 · 이전 대기 줄 폐기");
                }
                _pending.Enqueue((line, DateTime.UtcNow));
            }
            if (_pending.Count > 0 && _signal.CurrentCount == 0) _signal.Release();
        }
    }

    private async Task RunAsync()
    {
        try
        {
            while (true)
            {
                await _signal.WaitAsync(_stop.Token);
                while (!_stop.IsCancellationRequested)
                {
                    (string Text, DateTime Enqueued) item;
                    lock (_gate)
                    {
                        if (_pending.Count == 0) break;
                        item = _pending.Dequeue();
                        _active = item.Text;
                    }
                    try
                    {
                        if (DateTime.UtcNow - item.Enqueued > TimeSpan.FromSeconds(45))
                        {
                            _notice("번역 대기 만료 · 오래된 1줄 제외");
                            continue;
                        }
                        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                        deadline.CancelAfter(TimeSpan.FromSeconds(30));
                        // The callback must honor cancellation; never launch another GPU
                        // request while an abandoned callback is still running.
                        await _process(item.Text, deadline.Token);
                    }
                    catch (OperationCanceledException) when (_stop.IsCancellationRequested) { return; }
                    catch (OperationCanceledException) { _notice("번역 시간 초과 · 다음 줄 처리"); }
                    catch (Exception) { _notice("한 줄 번역 실패 · 다음 줄 처리"); }
                    finally { lock (_gate) _active = null; }
                }
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _pending.Clear();
            _stop.Cancel();
        }
        _ = _worker.ContinueWith(_ => { _stop.Dispose(); _signal.Dispose(); },
            CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }
}
