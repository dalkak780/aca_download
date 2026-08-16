using System.Text;

namespace ArcaDownloader.Core.Settings;

public sealed class AppSettingsStore
{
    private const string Section = "[General]";
    private const string OutputDirectoryKey = "OutputDirectory";
    private const string DownloadOriginalKey = "DownloadOriginal";
    private const string CleanupTempOnSuccessKey = "CleanupTempOnSuccess";

    public AppSettingsStore(string path)
    {
        Path = path;
    }

    public string Path { get; }

    public static AppSettingsStore Default()
    {
        return new AppSettingsStore(System.IO.Path.Combine(AppContext.BaseDirectory, "settings.ini"));
    }

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(Path))
        {
            return new AppSettings(null);
        }

        var lines = await File.ReadAllLinesAsync(Path, Encoding.UTF8, cancellationToken);
        var inGeneral = false;
        string? outputDirectory = null;
        var downloadOriginal = true;
        var cleanupTempOnSuccess = false;

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#'))
            {
                continue;
            }

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                inGeneral = line.Equals(Section, StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (!inGeneral)
            {
                continue;
            }

            var separator = line.IndexOf('=');
            if (separator < 0)
            {
                continue;
            }

            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();

            if (key.Equals(OutputDirectoryKey, StringComparison.OrdinalIgnoreCase))
            {
                outputDirectory = value;
            }
            else if (key.Equals(DownloadOriginalKey, StringComparison.OrdinalIgnoreCase))
            {
                if (bool.TryParse(value, out var parsedOriginal))
                {
                    downloadOriginal = parsedOriginal;
                }
            }
            else if (key.Equals(CleanupTempOnSuccessKey, StringComparison.OrdinalIgnoreCase))
            {
                if (bool.TryParse(value, out var parsedCleanup))
                {
                    cleanupTempOnSuccess = parsedCleanup;
                }
            }
        }

        return new AppSettings(
            string.IsNullOrWhiteSpace(outputDirectory) ? null : outputDirectory,
            downloadOriginal,
            cleanupTempOnSuccess);
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        var dir = System.IO.Path.GetDirectoryName(Path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var outputDirectory = (settings.OutputDirectory ?? "").ReplaceLineEndings("").Trim();
        var sb = new StringBuilder();
        sb.AppendLine(Section);
        sb.AppendLine($"{OutputDirectoryKey}={outputDirectory}");
        sb.AppendLine($"{DownloadOriginalKey}={settings.DownloadOriginal}");
        sb.AppendLine($"{CleanupTempOnSuccessKey}={settings.CleanupTempOnSuccess}");

        await File.WriteAllTextAsync(Path, sb.ToString(), Encoding.UTF8, cancellationToken);
    }
}
