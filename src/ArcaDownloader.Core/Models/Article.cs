namespace ArcaDownloader.Core.Models;

public sealed record Article(
    string Title,
    string Author,
    string Date,
    string SourceUrl,
    string ContentHtml,
    IReadOnlyList<ArticleImage> Images);

public sealed record ArticleImage(
    int Index,
    string SourceUrl,
    string FileName);

