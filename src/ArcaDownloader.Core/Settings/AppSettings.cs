namespace ArcaDownloader.Core.Settings;

public sealed record AppSettings(
    string? OutputDirectory,
    bool DownloadOriginal = true,
    bool CleanupTempOnSuccess = false);
