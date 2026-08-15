using System.Text.RegularExpressions;

namespace ArcaDownloader.Core.Services;

public static partial class UrlInputParser
{
    [GeneratedRegex(@"https?://[^\s<>""']+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HttpUrlRegex();

    public static IReadOnlyList<string> ExtractHttpUrls(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var urls = new List<string>();
        foreach (Match match in HttpUrlRegex().Matches(text))
        {
            if (TryGetHttpUrl(match.Value, out var url, out _))
            {
                urls.Add(url);
            }
        }

        return urls;
    }

    public static bool TryGetHttpUrl(string? rawUrl, out string url, out string normalizedUrl)
    {
        url = "";
        normalizedUrl = "";
        var candidate = CleanCandidate(rawUrl);
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https")
            || string.IsNullOrWhiteSpace(uri.Host))
        {
            return false;
        }

        var normalized = new UriBuilder(uri)
        {
            Fragment = ""
        }.Uri;

        url = candidate;
        normalizedUrl = normalized.AbsoluteUri;
        return true;
    }

    private static string CleanCandidate(string? rawUrl)
    {
        var candidate = (rawUrl ?? "").Trim();
        candidate = candidate.TrimStart('<', '>', '\"', '\'');
        candidate = candidate.TrimEnd('.', ',', ';', ':', '!', '?', ')', ']', '}');
        return candidate;
    }
}
