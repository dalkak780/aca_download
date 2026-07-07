using System.IO.Compression;
using System.Net;
using ArcaDownloader.Core.Auth;
using ArcaDownloader.Core.Download;
using ArcaDownloader.Core.Models;
using ArcaDownloader.Core.Services;
using Xunit;

namespace ArcaDownloader.Tests;

public sealed class CoreTests
{
    [Fact]
    public void Sanitizes_filename_like_python_app()
    {
        Assert.Equal("a-b-c", TextHelpers.SanitizeFileName("a / b:* c"));
        Assert.Equal("post", TextHelpers.SanitizeFileName("////"));
    }

    [Fact]
    public void Parses_cookie_header()
    {
        var cookies = CookieHeaderParser.Parse("a=1; b=two=2; invalid");
        Assert.Equal(new[] { ("a", "1"), ("b", "two=2") }, cookies);
    }

    [Fact]
    public async Task Saves_and_loads_cookie_jar()
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"arca-cookies-{Guid.NewGuid():N}.json");
        var jar = new CookieJar(path);

        await jar.SaveFromHeaderAsync("arca_auth=abc; other=1");
        var container = await jar.LoadAsync();
        var cookies = container.GetCookies(new Uri("https://arca.live/"));

        Assert.Equal("abc", cookies["arca_auth"]?.Value);
        File.Delete(path);
    }

    [Fact]
    public void Resolves_original_image_url_and_small_jpg_optimization()
    {
        var original = ArcaImageUrlResolver.Resolve(
            "https://ac-p1.namu.la/path/img.png?foo=1",
            null,
            null,
            "https://arca.live/b/test/1",
            null,
            true);
        Assert.Equal("https://ac-o.namu.la/path/img.png?foo=1&type=orig", original);

        var preview = ArcaImageUrlResolver.Resolve(
            "https://ac-p1.namu.la/path/small.jpg",
            null,
            null,
            "https://arca.live/b/test/1",
            "1280",
            true);
        Assert.Equal("https://ac-p1.namu.la/path/small.jpg", preview);
    }

    [Fact]
    public async Task Parses_article_fixture()
    {
        var html = await File.ReadAllTextAsync("TestData/article.html");
        var article = await new ArticleParser().ParseAsync(html, "https://arca.live/b/test/1#x", true);

        Assert.Equal("테스트 게시글", article.Title);
        Assert.Equal("작성자", article.Author);
        Assert.Equal("2026-07-07T12:00:00+09:00", article.Date);
        Assert.Equal("https://arca.live/b/test/1", article.SourceUrl);
        Assert.DoesNotContain("<script", article.ContentHtml);
        Assert.DoesNotContain("댓글", article.ContentHtml);
        Assert.Equal(2, article.Images.Count);
        Assert.Equal("img_001.jpg", article.Images[0].FileName);
        Assert.Equal("img_002.png", article.Images[1].FileName);
    }

    [Fact]
    public async Task Writes_expected_zip_entries()
    {
        var temp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"arca-zip-{Guid.NewGuid():N}");
        var article = new Article(
            "테스트",
            "작성자",
            "오늘",
            "https://arca.live/b/test/1",
            "<p><img src=\"https://ac-o.namu.la/a.png\" data-src=\"x\" loading=\"lazy\"></p>",
            [new ArticleImage(1, "https://ac-o.namu.la/a.png", "img_001.png")]);

        try
        {
            var path = await new ZipWriter().WriteAsync(article, new Dictionary<int, byte[]> { [1] = [1, 2, 3] }, temp);
            using (var archive = ZipFile.OpenRead(path))
            {
                Assert.NotNull(archive.GetEntry("post.html"));
                Assert.NotNull(archive.GetEntry("meta.txt"));
                Assert.NotNull(archive.GetEntry("images/img_001.png"));

                using var reader = new StreamReader(archive.GetEntry("post.html")!.Open());
                var postHtml = await reader.ReadToEndAsync();
                Assert.Contains("src=\"images/img_001.png\"", postHtml);
                Assert.DoesNotContain("data-src=", postHtml);
                Assert.DoesNotContain("loading=", postHtml);
            }
        }
        finally
        {
            if (Directory.Exists(temp))
            {
                Directory.Delete(temp, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Resume_store_reuses_successful_image_files()
    {
        var temp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"arca-resume-{Guid.NewGuid():N}");
        var article = new Article(
            "테스트",
            "작성자",
            "오늘",
            "https://arca.live/b/test/1",
            "<p>body</p>",
            [
                new ArticleImage(1, "https://ac-o.namu.la/a.png", "img_001.png"),
                new ArticleImage(2, "https://ac-o.namu.la/b.png", "img_002.png")
            ]);

        try
        {
            var store = DownloadResumeStore.ForUrl(temp, article.SourceUrl);
            await store.PrepareAsync(article);
            await store.SaveImageAsync(article.Images[0], [1, 2, 3]);

            var restored = await store.LoadImagesAsync(article.Images);

            Assert.Equal([1, 2, 3], restored[1]);
            Assert.False(restored.ContainsKey(2));
            Assert.True(File.Exists(Path.Combine(store.ImagesDirectory, "img_001.png")));
        }
        finally
        {
            if (Directory.Exists(temp))
            {
                Directory.Delete(temp, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Image_fetch_retries_429_then_succeeds()
    {
        var calls = 0;
        using var client = new HttpClient(new Handler(_ =>
        {
            calls++;
            return calls == 1
                ? new HttpResponseMessage((HttpStatusCode)429)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([7, 8, 9]) };
        }));

        var data = await DownloadService.FetchImageAsync(client, "https://example.test/image.png", null, null, CancellationToken.None);

        Assert.Equal([7, 8, 9], data);
        Assert.Equal(2, calls);
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(respond(request));
        }
    }
}
