using System.Text.Json;
using System.Text.Json.Serialization;
using ArcaDownloader.Core.Models;

namespace ArcaDownloader.Core.Download;

public sealed class DownloadQueueStore
{
    private readonly string _path;

    public DownloadQueueStore(string path)
    {
        _path = path;
    }

    public async Task<IReadOnlyList<DownloadQueueEntry>> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path))
        {
            return [];
        }

        await using var stream = File.OpenRead(_path);
        return await JsonSerializer.DeserializeAsync(
                   stream,
                   DownloadQueueJsonContext.Default.ListDownloadQueueEntry,
                   cancellationToken)
               ?? [];
    }

    public async Task SaveAsync(
        IEnumerable<DownloadQueueItem> items,
        CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporaryPath = _path + ".tmp";
        var entries = items
            .Select(item => new DownloadQueueEntry(item.Url, item.Status, item.ErrorMessage))
            .ToList();

        await using (var stream = new FileStream(
                         temporaryPath,
                         FileMode.Create,
                         FileAccess.Write,
                         FileShare.None,
                         4096,
                         FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            await JsonSerializer.SerializeAsync(
                stream,
                entries,
                DownloadQueueJsonContext.Default.ListDownloadQueueEntry,
                cancellationToken);
        }

        File.Move(temporaryPath, _path, overwrite: true);
    }
}

[JsonSerializable(typeof(List<DownloadQueueEntry>))]
internal sealed partial class DownloadQueueJsonContext : JsonSerializerContext
{
}
