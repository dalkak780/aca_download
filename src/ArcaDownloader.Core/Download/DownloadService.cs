using System.Diagnostics;
using System.Net;
using ArcaDownloader.Core.Models;
using ArcaDownloader.Core.Services;

namespace ArcaDownloader.Core.Download;

public sealed class DownloadService
{
    public const int MaxWorkers = 3;
    public const int FetchRetry = 10;
    public static readonly TimeSpan FetchWait = TimeSpan.FromSeconds(1);
    public static readonly TimeSpan ArticleTimeout = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan ImageIdleTimeout = TimeSpan.FromSeconds(60);

    private readonly ArticleParser _parser = new();
    private readonly ZipWriter _zipWriter = new();

    public async Task<DownloadResult> DownloadAsync(
        DownloadRequest request,
        CookieContainer cookieContainer,
        AsyncPauseGate pauseGate,
        IProgress<string>? log = null,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var uri = new Uri(request.Url);
        using var client = HttpClientFactory.Create(uri, cookieContainer);

        log?.Report($"[*] 요청: {request.Url}");
        if (!string.IsNullOrWhiteSpace(request.CookieHeader))
        {
            foreach (var pair in CookieHeaderParser.Parse(request.CookieHeader))
            {
                cookieContainer.Add(new Cookie(pair.Name, pair.Value, "/", "arca.live"));
            }
            log?.Report("    (쿠키 인증 사용 중)");
        }

        using var articleCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        articleCts.CancelAfter(ArticleTimeout);
        HttpResponseMessage response;
        try
        {
            response = await client.GetAsync(uri, articleCts.Token);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"게시글 요청 시간이 {ArticleTimeout.TotalSeconds:0}초를 초과했습니다.", ex);
        }

        using (response)
        {
            if ((int)response.StatusCode is 401 or 403 or 451)
            {
                throw new AuthenticationRequiredException($"HTTP {(int)response.StatusCode}: 유효한 아카라이브 로그인 쿠키가 필요합니다.");
            }
            response.EnsureSuccessStatusCode();

            var html = await response.Content.ReadAsStringAsync(cancellationToken);
            var article = await _parser.ParseAsync(html, request.Url, request.DownloadOriginal, cancellationToken);

            log?.Report($"    제목  : {article.Title}");
            log?.Report($"    작성자: {Fallback(article.Author)}");
            log?.Report($"    작성일: {Fallback(article.Date)}");

            var total = article.Images.Count;
            log?.Report(request.DownloadOriginal
                ? $"[*] 이미지 {total}개 순차 다운로드 (ArcaRefresher 방식, 최대 {FetchRetry}회 재시도)..."
                : $"[*] 이미지 {total}개 다운로드 (워커 {Math.Min(MaxWorkers, Math.Max(total, 1))}개)...");

            var resumeStore = DownloadResumeStore.ForUrl(request.OutputDirectory, request.Url);
            await resumeStore.PrepareAsync(article, cancellationToken);
            var cachedImages = await resumeStore.LoadImagePathsAsync(article.Images, cancellationToken);
            if (cachedImages.Count > 0)
            {
                log?.Report($"[*] 이전 성공분 {cachedImages.Count}개를 재사용합니다: {resumeStore.ImagesDirectory}");
            }
            else
            {
                log?.Report($"[*] 재개 기록 위치: {resumeStore.ImagesDirectory}");
            }

            progress?.Report(new DownloadProgress(cachedImages.Count, total, 0, 0, 0, null, null));

            var images = request.DownloadOriginal
                ? await DownloadSequentialAsync(client, article.Images, cachedImages, resumeStore, pauseGate, log, progress, cancellationToken)
                : await DownloadPreviewAsync(client, article.Images, cachedImages, resumeStore, log, progress, cancellationToken);

            var zipPath = await _zipWriter.WriteAsync(article, images, request.OutputDirectory, cancellationToken);
            return new DownloadResult(zipPath, images.Count, total);
        }
    }

    private static async Task<Dictionary<int, string>> DownloadSequentialAsync(
        HttpClient client,
        IReadOnlyList<ArticleImage> images,
        Dictionary<int, string> cachedImages,
        DownloadResumeStore resumeStore,
        AsyncPauseGate pauseGate,
        IProgress<string>? log,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        var results = new Dictionary<int, string>(cachedImages);
        var completedSizes = cachedImages.Values.Select(path => new FileInfo(path).Length).ToList();
        var global = Stopwatch.StartNew();

        foreach (var image in images)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (results.ContainsKey(image.Index))
            {
                log?.Report($"  [{image.Index:000}/{images.Count}] 재사용: {image.FileName}");
                progress?.Report(new DownloadProgress(results.Count, images.Count, 0, 0, 0, null, null));
                continue;
            }

            log?.Report($"  [{image.Index:000}/{images.Count}] {image.SourceUrl}");

            var imageWatch = Stopwatch.StartNew();
            var destinationPath = resumeStore.GetImagePath(image);
            var bytesWritten = await FetchImageToFileAsync(client, image.SourceUrl, destinationPath, log, async (downloaded, total) =>
            {
                await pauseGate.WaitAsync(cancellationToken);
                var speed = imageWatch.Elapsed.TotalSeconds > 0 ? downloaded / imageWatch.Elapsed.TotalSeconds : 0;
                TimeSpan? imageEta = null;
                if (total > 0 && speed > 0)
                {
                    imageEta = TimeSpan.FromSeconds(Math.Max(0, (total - downloaded) / speed));
                }

                TimeSpan? totalEta = null;
                if (total > 0 && global.Elapsed.TotalSeconds > 0)
                {
                    var fraction = downloaded / (double)total;
                    var effectiveDone = completedSizes.Count + fraction;
                    var totalDownloaded = completedSizes.Sum() + downloaded;
                    if (effectiveDone > 0 && totalDownloaded > 0)
                    {
                        var averageImageSize = totalDownloaded / effectiveDone;
                        var overallSpeed = totalDownloaded / global.Elapsed.TotalSeconds;
                        var remaining = (images.Count - image.Index + 1 - fraction) * averageImageSize;
                        totalEta = overallSpeed > 0 ? TimeSpan.FromSeconds(remaining / overallSpeed) : null;
                    }
                }

                progress?.Report(new DownloadProgress(results.Count, images.Count, downloaded, total, speed, imageEta, totalEta));
            }, cancellationToken);

            if (bytesWritten > 0)
            {
                results[image.Index] = destinationPath;
                completedSizes.Add(bytesWritten);
            }
            else
            {
                log?.Report("       → 건너뜀");
            }

            progress?.Report(new DownloadProgress(results.Count, images.Count, 0, 0, 0, null, null));
        }

        return results;
    }

    private static async Task<Dictionary<int, string>> DownloadPreviewAsync(
        HttpClient client,
        IReadOnlyList<ArticleImage> images,
        Dictionary<int, string> cachedImages,
        DownloadResumeStore resumeStore,
        IProgress<string>? log,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        var results = new Dictionary<int, string>(cachedImages);
        var done = cachedImages.Count;
        var lockObject = new object();
        var throttle = new SemaphoreSlim(1, 1);
        var lastRequest = DateTimeOffset.MinValue;

        async Task DownloadOneAsync(ArticleImage image)
        {
            lock (lockObject)
            {
                if (results.ContainsKey(image.Index))
                {
                    log?.Report($"  [{image.Index:000}/{images.Count}] 재사용: {image.FileName}");
                    progress?.Report(new DownloadProgress(done, images.Count, 0, 0, 0, null, null));
                    return;
                }
            }

            await throttle.WaitAsync(cancellationToken);
            try
            {
                var wait = TimeSpan.FromMilliseconds(500) - (DateTimeOffset.UtcNow - lastRequest);
                if (wait > TimeSpan.Zero) await Task.Delay(wait, cancellationToken);
                lastRequest = DateTimeOffset.UtcNow;
            }
            finally
            {
                throttle.Release();
            }

            log?.Report($"  [{image.Index:000}/{images.Count}] {image.SourceUrl}");
            var destinationPath = resumeStore.GetImagePath(image);
            var bytesWritten = await FetchImageToFileAsync(client, image.SourceUrl, destinationPath, log, null, cancellationToken);

            lock (lockObject)
            {
                if (bytesWritten > 0) results[image.Index] = destinationPath;
                else log?.Report("       → 건너뜀");
                done++;
                progress?.Report(new DownloadProgress(done, images.Count, 0, 0, 0, null, null));
            }
        }

        await Parallel.ForEachAsync(images, new ParallelOptions
        {
            MaxDegreeOfParallelism = MaxWorkers,
            CancellationToken = cancellationToken
        }, async (image, token) => await DownloadOneAsync(image));

        return results;
    }

    internal static async Task<long> FetchImageToFileAsync(
        HttpClient client,
        string sourceUrl,
        string destinationPath,
        IProgress<string>? log,
        Func<long, long, Task>? chunkProgress,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= FetchRetry; attempt++)
        {
            try
            {
                using var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                attemptCts.CancelAfter(ImageIdleTimeout);
                using var response = await client.GetAsync(sourceUrl, HttpCompletionOption.ResponseHeadersRead, attemptCts.Token);
                if ((int)response.StatusCode == 429)
                {
                    log?.Report($"    [WARN] 429 — {FetchWait.TotalSeconds:0}s 대기 재시도 ({attempt}/{FetchRetry})");
                    await Task.Delay(FetchWait, cancellationToken);
                    continue;
                }

                response.EnsureSuccessStatusCode();
                var total = response.Content.Headers.ContentLength ?? -1;
                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath) ?? ".");
                var partialPath = destinationPath + ".part";
                await using var input = await response.Content.ReadAsStreamAsync(attemptCts.Token);
                await using var output = File.Create(partialPath);
                var buffer = new byte[8192];
                long bytesWritten = 0;
                int read;
                while ((read = await input.ReadAsync(buffer, attemptCts.Token)) > 0)
                {
                    attemptCts.CancelAfter(ImageIdleTimeout);
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    bytesWritten += read;
                    if (chunkProgress is not null)
                    {
                        await chunkProgress(bytesWritten, total);
                    }
                }

                await output.FlushAsync(cancellationToken);
                output.Close();
                File.Move(partialPath, destinationPath, overwrite: true);
                return bytesWritten;
            }
            catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested && attempt < FetchRetry)
            {
                TryDeletePartial(destinationPath);
                log?.Report($"    [WARN] 실패: {ex.Message} — {FetchWait.TotalSeconds:0}s 후 재시도 ({attempt}/{FetchRetry})");
                await Task.Delay(FetchWait, cancellationToken);
            }
            catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                TryDeletePartial(destinationPath);
                log?.Report($"    [WARN] 최종 실패 ({FetchRetry}회): {ex.Message}");
            }
            catch (Exception ex) when (ex is not OperationCanceledException && attempt < FetchRetry)
            {
                TryDeletePartial(destinationPath);
                log?.Report($"    [WARN] 실패: {ex.Message} — {FetchWait.TotalSeconds:0}s 후 재시도 ({attempt}/{FetchRetry})");
                await Task.Delay(FetchWait, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                TryDeletePartial(destinationPath);
                log?.Report($"    [WARN] 최종 실패 ({FetchRetry}회): {ex.Message}");
            }
        }

        return 0;
    }

    private static string Fallback(string value) => string.IsNullOrWhiteSpace(value) ? "Unknown" : value;

    private static void TryDeletePartial(string destinationPath)
    {
        var partialPath = destinationPath + ".part";
        try
        {
            if (File.Exists(partialPath))
            {
                File.Delete(partialPath);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

public sealed class AuthenticationRequiredException(string message) : Exception(message);
