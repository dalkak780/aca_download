namespace ArcaDownloader.Core.Services;

public sealed class AsyncPauseGate
{
    private volatile TaskCompletionSource? _paused;

    public bool IsPaused => _paused is not null;

    public void Pause()
    {
        Interlocked.CompareExchange(
            ref _paused,
            new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously),
            null);
    }

    public void Resume()
    {
        var paused = Interlocked.Exchange(ref _paused, null);
        paused?.TrySetResult();
    }

    public async Task WaitAsync(CancellationToken cancellationToken)
    {
        while (_paused is { } paused)
        {
            await paused.Task.WaitAsync(cancellationToken);
        }
    }
}

