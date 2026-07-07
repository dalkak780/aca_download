namespace ArcaDownloader.Core.Services;

public static class ArcaImageUrlResolver
{
    public static readonly ISet<string> ImageExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "png", "jpg", "jpeg", "gif", "webp", "avif", "bmp", "svg"
    };

    public static string Resolve(string? dataOriginalUrl, string? dataSrc, string? src, string baseUrl, string? width, bool downloadOriginal)
    {
        var raw = FirstNonEmpty(dataOriginalUrl, dataSrc, src);
        if (string.IsNullOrWhiteSpace(raw)) return "";

        if (!Uri.TryCreate(new Uri(baseUrl), raw, out var absolute))
        {
            return raw;
        }

        if (!downloadOriginal) return absolute.ToString();

        var extension = GetImageExtension(absolute.ToString());
        if ((extension.Equals("jpg", StringComparison.OrdinalIgnoreCase) ||
             extension.Equals("jpeg", StringComparison.OrdinalIgnoreCase)) &&
            int.TryParse(width, out var parsedWidth) &&
            parsedWidth is > 0 and <= 1280)
        {
            return absolute.ToString();
        }

        if (absolute.Host.Contains("namu.la", StringComparison.OrdinalIgnoreCase) ||
            absolute.Host.Contains("arca.live", StringComparison.OrdinalIgnoreCase))
        {
            var builder = new UriBuilder(absolute)
            {
                Host = "ac-o.namu.la"
            };
            builder.Query = AddOrReplaceQuery(builder.Query, "type", "orig");
            return builder.Uri.ToString();
        }

        return absolute.ToString();
    }

    public static string GetImageExtension(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            var last = Path.GetFileName(uri.LocalPath);
            var ext = Path.GetExtension(last).TrimStart('.').ToLowerInvariant();
            if (ImageExtensions.Contains(ext)) return ext;
        }

        return "png";
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value)) return value;
        }

        return null;
    }

    private static string AddOrReplaceQuery(string query, string name, string value)
    {
        var parts = query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Where(part => !part.StartsWith($"{Uri.EscapeDataString(name)}=", StringComparison.OrdinalIgnoreCase) &&
                           !part.Equals(Uri.EscapeDataString(name), StringComparison.OrdinalIgnoreCase))
            .ToList();
        parts.Add($"{Uri.EscapeDataString(name)}={Uri.EscapeDataString(value)}");
        return string.Join('&', parts);
    }
}
