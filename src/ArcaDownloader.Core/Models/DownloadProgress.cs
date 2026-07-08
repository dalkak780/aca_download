namespace ArcaDownloader.Core.Models;

public sealed record DownloadProgress(
    int DoneImages,
    int TotalImages,
    long CurrentImageBytes,
    long CurrentImageTotalBytes,
    double CurrentImageBytesPerSecond,
    TimeSpan? CurrentImageEta,
    TimeSpan? TotalEta);

public sealed record DownloadResult(
    string ZipPath,
    int DownloadedImages,
    int TotalImages);

public sealed record DownloadRequest(
    string Url,
    string OutputDirectory,
    string CookieHeader,
    bool DownloadOriginal,
    bool CleanupTempOnSuccess = false);
