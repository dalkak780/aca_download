namespace ArcaDownloader.Core.Models;

public enum DownloadQueueItemStatus
{
    Pending,
    Downloading,
    Failed
}

public sealed record DownloadQueueEntry(
    string Url,
    DownloadQueueItemStatus Status,
    string? ErrorMessage);

public sealed class DownloadQueueItem
{
    internal DownloadQueueItem(
        string url,
        string normalizedUrl,
        DownloadQueueItemStatus status,
        string? errorMessage)
    {
        Url = url;
        NormalizedUrl = normalizedUrl;
        Status = status;
        ErrorMessage = errorMessage;
    }

    public string Url { get; }

    internal string NormalizedUrl { get; }

    public DownloadQueueItemStatus Status { get; private set; }

    public string? ErrorMessage { get; private set; }

    internal void MarkDownloading()
    {
        Status = DownloadQueueItemStatus.Downloading;
        ErrorMessage = null;
    }

    internal void MarkPending()
    {
        Status = DownloadQueueItemStatus.Pending;
        ErrorMessage = null;
    }

    internal void MarkFailed(string errorMessage)
    {
        Status = DownloadQueueItemStatus.Failed;
        ErrorMessage = errorMessage;
    }
}
