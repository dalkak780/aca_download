using ArcaDownloader.Core.Models;
using ArcaDownloader.Core.Services;

namespace ArcaDownloader.Core.Download;

public sealed class DownloadQueue
{
    private readonly List<DownloadQueueItem> _items = [];

    public DownloadQueue(IEnumerable<DownloadQueueEntry>? entries = null)
    {
        if (entries is null)
        {
            return;
        }

        foreach (var entry in entries)
        {
            if (!UrlInputParser.TryGetHttpUrl(entry.Url, out var url, out var normalizedUrl))
            {
                continue;
            }

            var status = entry.Status == DownloadQueueItemStatus.Downloading
                ? DownloadQueueItemStatus.Pending
                : entry.Status;
            _items.Add(new DownloadQueueItem(url, normalizedUrl, status, entry.ErrorMessage));
        }
    }

    public IReadOnlyList<DownloadQueueItem> Items => _items;

    public IReadOnlyList<string> FindDuplicates(IEnumerable<string> urls)
    {
        var known = _items
            .Select(item => item.NormalizedUrl)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var duplicates = new List<string>();

        foreach (var rawUrl in urls)
        {
            if (!UrlInputParser.TryGetHttpUrl(rawUrl, out var url, out var normalizedUrl))
            {
                continue;
            }

            if (!known.Add(normalizedUrl))
            {
                duplicates.Add(url);
            }
        }

        return duplicates;
    }

    public IReadOnlyList<DownloadQueueItem> Add(IEnumerable<string> urls, bool includeDuplicates)
    {
        var known = _items
            .Select(item => item.NormalizedUrl)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var added = new List<DownloadQueueItem>();

        foreach (var rawUrl in urls)
        {
            if (!UrlInputParser.TryGetHttpUrl(rawUrl, out var url, out var normalizedUrl))
            {
                continue;
            }

            if (!includeDuplicates && !known.Add(normalizedUrl))
            {
                continue;
            }

            var item = new DownloadQueueItem(
                url,
                normalizedUrl,
                DownloadQueueItemStatus.Pending,
                null);
            _items.Add(item);
            added.Add(item);
        }

        return added;
    }

    public bool TryTakeNextPending(out DownloadQueueItem item)
    {
        item = _items.FirstOrDefault(candidate => candidate.Status == DownloadQueueItemStatus.Pending)!;
        if (item is null)
        {
            return false;
        }

        item.MarkDownloading();
        return true;
    }

    public void MarkCompleted(DownloadQueueItem item)
    {
        EnsureContains(item);
        _items.Remove(item);
    }

    public void MarkFailed(DownloadQueueItem item, string errorMessage)
    {
        EnsureContains(item);
        item.MarkFailed(errorMessage);
    }

    public void MarkPending(DownloadQueueItem item)
    {
        EnsureContains(item);
        item.MarkPending();
    }

    public void Retry(DownloadQueueItem item)
    {
        EnsureContains(item);
        if (item.Status == DownloadQueueItemStatus.Failed)
        {
            item.MarkPending();
        }
    }

    public void Remove(DownloadQueueItem item)
    {
        EnsureContains(item);
        if (item.Status == DownloadQueueItemStatus.Downloading)
        {
            throw new InvalidOperationException("현재 다운로드 중인 항목은 중지 후 삭제할 수 있습니다.");
        }

        _items.Remove(item);
    }

    public int ClearWaiting()
    {
        return _items.RemoveAll(item => item.Status != DownloadQueueItemStatus.Downloading);
    }

    public IReadOnlyList<DownloadQueueEntry> ToEntries()
    {
        return _items
            .Select(item => new DownloadQueueEntry(item.Url, item.Status, item.ErrorMessage))
            .ToList();
    }

    private void EnsureContains(DownloadQueueItem item)
    {
        if (!_items.Contains(item))
        {
            throw new ArgumentException("큐에 없는 항목입니다.", nameof(item));
        }
    }
}
