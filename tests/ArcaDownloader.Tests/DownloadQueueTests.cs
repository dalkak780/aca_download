using ArcaDownloader.Core.Download;
using ArcaDownloader.Core.Models;
using ArcaDownloader.Core.Services;
using Xunit;

namespace ArcaDownloader.Tests;

public sealed class DownloadQueueTests
{
    [Fact]
    public void Extracts_http_urls_from_mixed_clipboard_text()
    {
        var text = "첫 번째 https://arca.live/b/test/1, 설명\nhttps://arca.live/b/test/2#comment.";

        var urls = UrlInputParser.ExtractHttpUrls(text);

        Assert.Equal(
            ["https://arca.live/b/test/1", "https://arca.live/b/test/2#comment"],
            urls);
    }

    [Fact]
    public void Detects_existing_and_same_batch_duplicates_after_normalization()
    {
        var queue = new DownloadQueue();
        queue.Add(["https://arca.live/b/test/1"], includeDuplicates: false);

        var duplicates = queue.FindDuplicates(
            [
                "https://arca.live/b/test/1#comment",
                "https://arca.live/b/test/2",
                "https://arca.live/b/test/2"
            ]);

        Assert.Equal(
            ["https://arca.live/b/test/1#comment", "https://arca.live/b/test/2"],
            duplicates);
    }

    [Fact]
    public void Skips_duplicates_without_confirmation_and_keeps_them_when_confirmed()
    {
        var queue = new DownloadQueue();
        queue.Add(["https://arca.live/b/test/1"], includeDuplicates: false);

        var skipped = queue.Add(
            ["https://arca.live/b/test/1#comment", "https://arca.live/b/test/2"],
            includeDuplicates: false);
        var included = queue.Add(
            ["https://arca.live/b/test/1#comment"],
            includeDuplicates: true);

        Assert.Single(skipped);
        Assert.Equal("https://arca.live/b/test/2", skipped[0].Url);
        Assert.Single(included);
        Assert.Equal(3, queue.Items.Count);
    }

    [Fact]
    public void Ignores_non_http_inputs_and_strips_fragment_from_duplicate_identity()
    {
        var queue = new DownloadQueue();

        var added = queue.Add(
            ["ftp://arca.live/b/test/1", "not a url", "https://arca.live/b/test/1#comment"],
            includeDuplicates: false);
        var duplicates = queue.FindDuplicates(["https://arca.live/b/test/1"]);

        Assert.Single(added);
        Assert.Equal("https://arca.live/b/test/1#comment", added[0].Url);
        Assert.Equal(["https://arca.live/b/test/1"], duplicates);
    }

    [Fact]
    public void Failed_items_are_skipped_until_retried_and_completed_items_are_removed()
    {
        var queue = new DownloadQueue();
        var added = queue.Add(
            ["https://arca.live/b/test/1", "https://arca.live/b/test/2"],
            includeDuplicates: false);

        Assert.True(queue.TryTakeNextPending(out var first));
        queue.MarkFailed(first, "network error");
        Assert.Equal(DownloadQueueItemStatus.Failed, first.Status);

        Assert.True(queue.TryTakeNextPending(out var second));
        queue.MarkCompleted(second);
        Assert.DoesNotContain(second, queue.Items);

        queue.Retry(first);
        Assert.True(queue.TryTakeNextPending(out var retried));
        Assert.Same(first, retried);
    }

    [Fact]
    public void Restored_downloading_item_returns_to_pending()
    {
        var queue = new DownloadQueue(
        [
            new DownloadQueueEntry(
                "https://arca.live/b/test/1",
                DownloadQueueItemStatus.Downloading,
                null),
            new DownloadQueueEntry(
                "https://arca.live/b/test/2",
                DownloadQueueItemStatus.Failed,
                "broken")
        ]);

        Assert.Equal(DownloadQueueItemStatus.Pending, queue.Items[0].Status);
        Assert.Equal(DownloadQueueItemStatus.Failed, queue.Items[1].Status);
        Assert.True(queue.TryTakeNextPending(out var item));
        Assert.Equal("https://arca.live/b/test/1", item.Url);
    }

    [Fact]
    public void Clear_waiting_removes_pending_and_failed_but_keeps_active_item()
    {
        var queue = new DownloadQueue();
        var added = queue.Add(
            ["https://arca.live/b/test/1", "https://arca.live/b/test/2", "https://arca.live/b/test/3"],
            includeDuplicates: false);
        Assert.True(queue.TryTakeNextPending(out var active));
        queue.MarkFailed(added[1], "broken");

        var removed = queue.ClearWaiting();

        Assert.Equal(2, removed);
        Assert.Single(queue.Items);
        Assert.Same(active, queue.Items[0]);
    }

    [Fact]
    public async Task Queue_store_round_trips_items_and_status()
    {
        var path = Path.Combine(Path.GetTempPath(), $"arca-queue-{Guid.NewGuid():N}", "queue.json");
        var queue = new DownloadQueue();
        queue.Add(["https://arca.live/b/test/1"], includeDuplicates: false);
        Assert.True(queue.TryTakeNextPending(out var item));
        queue.MarkFailed(item, "broken");

        try
        {
            var store = new DownloadQueueStore(path);
            await store.SaveAsync(queue.Items);
            var restored = await store.LoadAsync();

            Assert.Single(restored);
            Assert.Equal("https://arca.live/b/test/1", restored[0].Url);
            Assert.Equal(DownloadQueueItemStatus.Failed, restored[0].Status);
            Assert.Equal("broken", restored[0].ErrorMessage);
        }
        finally
        {
            var directory = Path.GetDirectoryName(path);
            if (directory is not null && Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
