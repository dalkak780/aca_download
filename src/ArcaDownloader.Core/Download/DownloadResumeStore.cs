using System.Security.Cryptography;
using System.Text;
using ArcaDownloader.Core.Models;
using ArcaDownloader.Core.Services;

namespace ArcaDownloader.Core.Download;

public sealed class DownloadResumeStore
{
    private const string StateRootName = ".arca_tmp";

    private DownloadResumeStore(string rootDirectory)
    {
        RootDirectory = rootDirectory;
        ImagesDirectory = Path.Combine(rootDirectory, "images");
    }

    public string RootDirectory { get; }
    public string ImagesDirectory { get; }

    public static DownloadResumeStore ForUrl(string outputDirectory, string url)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url)))[..16].ToLowerInvariant();
        return new DownloadResumeStore(Path.Combine(outputDirectory, StateRootName, hash));
    }

    public async Task PrepareAsync(Article article, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(ImagesDirectory);

        var meta = string.Join(Environment.NewLine,
            $"Source: {article.SourceUrl}",
            $"Title: {article.Title}",
            $"Updated: {DateTimeOffset.Now:O}");
        await File.WriteAllTextAsync(Path.Combine(RootDirectory, "resume.txt"), meta, Encoding.UTF8, cancellationToken);
    }

    public Task<Dictionary<int, string>> LoadImagePathsAsync(
        IReadOnlyList<ArticleImage> images,
        CancellationToken cancellationToken = default)
    {
        var restored = new Dictionary<int, string>();
        foreach (var image in images)
        {
            var path = GetImagePath(image);
            if (!File.Exists(path))
            {
                continue;
            }

            var file = new FileInfo(path);
            if (file.Length <= 0)
            {
                continue;
            }

            restored[image.Index] = path;
        }

        return Task.FromResult(restored);
    }

    public async Task SaveImageAsync(
        ArticleImage image,
        byte[] data,
        CancellationToken cancellationToken = default)
    {
        if (data.Length == 0)
        {
            return;
        }

        Directory.CreateDirectory(ImagesDirectory);
        var path = GetImagePath(image);
        var partialPath = path + ".part";

        await File.WriteAllBytesAsync(partialPath, data, cancellationToken);
        File.Move(partialPath, path, overwrite: true);
    }

    public string GetImagePath(ArticleImage image)
    {
        var fileName = TextHelpers.SanitizeFileName(Path.GetFileNameWithoutExtension(image.FileName), 120);
        var extension = Path.GetExtension(image.FileName);
        if (string.IsNullOrWhiteSpace(extension))
        {
            extension = ".bin";
        }

        return Path.Combine(ImagesDirectory, $"{fileName}{extension}");
    }

    public void Delete()
    {
        if (Directory.Exists(RootDirectory))
        {
            Directory.Delete(RootDirectory, recursive: true);
        }
    }
}
