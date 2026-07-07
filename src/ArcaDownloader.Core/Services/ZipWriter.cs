using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using ArcaDownloader.Core.Models;

namespace ArcaDownloader.Core.Services;

public sealed class ZipWriter
{
    public async Task<string> WriteAsync(
        Article article,
        IReadOnlyDictionary<int, byte[]> downloadedImages,
        string outputDirectory,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(outputDirectory);
        var zipPath = Path.Combine(outputDirectory, $"arca-{TextHelpers.SanitizeFileName(article.Title)}.zip");

        await using var file = File.Create(zipPath);
        using var archive = new ZipArchive(file, ZipArchiveMode.Create);

        var downloadedNames = new List<string>();
        var contentHtml = article.ContentHtml;
        foreach (var image in article.Images)
        {
            if (!downloadedImages.TryGetValue(image.Index, out var data) || data.Length == 0)
            {
                continue;
            }

            var entry = archive.CreateEntry($"images/{image.FileName}", CompressionLevel.Optimal);
            await using (var entryStream = entry.Open())
            {
                await entryStream.WriteAsync(data, cancellationToken);
            }

            downloadedNames.Add(image.FileName);
            contentHtml = RewriteImageTag(contentHtml, image.SourceUrl, $"images/{image.FileName}");
        }

        await WriteTextEntryAsync(archive, "post.html", BuildPostHtml(article, contentHtml), cancellationToken);

        var imageLine = downloadedNames.Count > 0
            ? $"Images: {downloadedNames.Count} files under /images"
            : "Images: (none downloaded)";
        var meta = string.Join('\n',
            $"Title: {article.Title}",
            $"Author: {Fallback(article.Author)}",
            $"Date: {Fallback(article.Date)}",
            $"Source: {article.SourceUrl}",
            imageLine);
        await WriteTextEntryAsync(archive, "meta.txt", meta, cancellationToken);

        return Path.GetFullPath(zipPath);
    }

    public static string BuildPostHtml(Article article, string contentHtml)
    {
        var title = TextHelpers.EscapeHtml(article.Title);
        var date = TextHelpers.EscapeHtml(Fallback(article.Date));
        var author = TextHelpers.EscapeHtml(Fallback(article.Author));
        var source = TextHelpers.EscapeHtml(article.SourceUrl);

        var header = "<header style=\"font:14px/1.5 system-ui,sans-serif;border-bottom:1px solid #ddd;padding:12px 0;margin-bottom:16px;\">" +
                     $"<div><strong>제목</strong>: {title}</div><div><strong>작성일</strong>: {date}</div>" +
                     $"<div><strong>작성자</strong>: {author}</div>" +
                     $"<div><strong>원문</strong>: <a href=\"{source}\">{source}</a></div></header>";

        return "<!doctype html><html lang=\"ko\"><head><meta charset=\"utf-8\">" +
               "<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">" +
               $"<title>{title}</title>" +
               "<style>body{max-width:960px;margin:0 auto;padding:24px;" +
               "font:16px/1.7 system-ui,sans-serif;color:#111;background:#fff}" +
               "img{max-width:100%;height:auto}" +
               "pre,code{white-space:pre-wrap;word-break:break-word}" +
               "table{border-collapse:collapse}td,th{border:1px solid #ddd;padding:6px}" +
               $"</style></head><body>{header}<main>{contentHtml}</main></body></html>";
    }

    private static async Task WriteTextEntryAsync(ZipArchive archive, string name, string text, CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        await using var stream = entry.Open();
        await stream.WriteAsync(Encoding.UTF8.GetBytes(text), cancellationToken);
    }

    private static string RewriteImageTag(string html, string oldSrc, string newSrc)
    {
        var escapedOld = TextHelpers.EscapeHtml(oldSrc);
        var oldAlternatives = $"{Regex.Escape(oldSrc)}|{Regex.Escape(escapedOld)}";
        return Regex.Replace(
            html,
            $@"<img\b(?=[^>]*\bsrc\s*=\s*[""'](?:{oldAlternatives})[""'])[^>]*>",
            match =>
            {
                var tag = match.Value;
                tag = Regex.Replace(tag, @"\s(?:srcset|data-src|loading)\s*=\s*[""'][^""']*[""']", "", RegexOptions.IgnoreCase);
                tag = Regex.Replace(tag, $@"\bsrc\s*=\s*[""'](?:{oldAlternatives})[""']", $"src=\"{newSrc}\"", RegexOptions.IgnoreCase);
                return tag;
            },
            RegexOptions.IgnoreCase);
    }

    private static string Fallback(string value) => string.IsNullOrWhiteSpace(value) ? "Unknown" : value;
}
