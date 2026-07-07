using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using ArcaDownloader.Core.Models;

namespace ArcaDownloader.Core.Services;

public sealed class ArticleParser
{
    private static readonly string[] BodySelectors =
    [
        "#article-content", ".article-content", ".content .fr-view",
        ".fr-view", ".article-body", ".content-body",
        "article .content", ".markdown-body", ".article .content-body"
    ];

    private static readonly string[] StripTags =
    [
        "script", "style", "iframe", "video", "audio", "noscript"
    ];

    private static readonly string[] StripSelectors =
    [
        ".btn", ".buttons", ".actions", ".toolbar",
        ".comment", ".comments", ".ad", "[data-ad]"
    ];

    private readonly HtmlParser _parser = new();

    public async Task<Article> ParseAsync(string html, string url, bool downloadOriginal, CancellationToken cancellationToken = default)
    {
        var document = await _parser.ParseDocumentAsync(html, cancellationToken);

        var title = document.QuerySelector("meta[property='og:title']")?.GetAttribute("content")
                    ?? document.Title
                    ?? "post";
        title = title.Trim();

        var authorElement = document.QuerySelector("[rel='author']")
                            ?? document.QuerySelector(".article-header .user,.user-info .nick,.writer,.author");
        var authorMeta = document.QuerySelector("meta[name='author']")?.GetAttribute("content");
        var author = (authorElement?.TextContent.Trim() ?? authorMeta ?? "").Trim();

        var date = document.QuerySelector("time[datetime]")?.GetAttribute("datetime")?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(date))
        {
            date = document.QuerySelector("time")?.TextContent.Trim() ?? "";
        }
        if (string.IsNullOrWhiteSpace(date))
        {
            date = document.QuerySelector(".date,.time,.article-info time")?.TextContent.Trim() ?? "";
        }

        var body = BodySelectors
            .Select(selector => document.QuerySelector(selector))
            .FirstOrDefault(element => element is not null);

        if (body is null)
        {
            throw new InvalidOperationException("본문을 찾지 못했어요.");
        }

        var content = body.Clone(true) as IElement
                      ?? throw new InvalidOperationException("본문 복사에 실패했습니다.");

        foreach (var tagName in StripTags)
        {
            foreach (var node in content.QuerySelectorAll(tagName).ToArray())
            {
                node.Remove();
            }
        }

        foreach (var selector in StripSelectors)
        {
            foreach (var node in content.QuerySelectorAll(selector).ToArray())
            {
                node.Remove();
            }
        }

        var images = new List<ArticleImage>();
        var index = 1;
        foreach (var image in content.QuerySelectorAll("img"))
        {
            var resolved = ArcaImageUrlResolver.Resolve(
                image.GetAttribute("data-originalurl"),
                image.GetAttribute("data-src"),
                image.GetAttribute("src"),
                url,
                image.GetAttribute("width"),
                downloadOriginal);

            if (string.IsNullOrWhiteSpace(resolved))
            {
                var fallback = image.GetAttribute("data-src");
                if (!string.IsNullOrWhiteSpace(fallback))
                {
                    image.SetAttribute("src", fallback);
                    resolved = fallback;
                }
            }
            else
            {
                image.SetAttribute("src", resolved);
            }

            if (string.IsNullOrWhiteSpace(resolved)) continue;

            if (Uri.TryCreate(new Uri(url), resolved, out var absolute))
            {
                resolved = absolute.ToString();
            }

            var fileName = $"img_{index:000}.{ArcaImageUrlResolver.GetImageExtension(resolved)}";
            images.Add(new ArticleImage(index, resolved, fileName));
            index++;
        }

        cancellationToken.ThrowIfCancellationRequested();
        return new Article(
            string.IsNullOrWhiteSpace(title) ? "post" : title,
            author,
            date,
            url.Split('#')[0],
            content.InnerHtml,
            images);
    }
}
