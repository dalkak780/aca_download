using System.Net;

namespace ArcaDownloader.Core.Services;

public static class CookieHeaderParser
{
    public static CookieContainer ToCookieContainer(string cookieHeader)
    {
        var container = new CookieContainer();
        foreach (var (name, value) in Parse(cookieHeader))
        {
            container.Add(new Cookie(name, value, "/", "arca.live"));
        }

        return container;
    }

    public static IReadOnlyList<(string Name, string Value)> Parse(string cookieHeader)
    {
        if (string.IsNullOrWhiteSpace(cookieHeader)) return [];

        var cookies = new List<(string Name, string Value)>();
        foreach (var rawPair in cookieHeader.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var equals = rawPair.IndexOf('=');
            if (equals <= 0) continue;

            var name = rawPair[..equals].Trim();
            var value = rawPair[(equals + 1)..].Trim();
            if (name.Length > 0)
            {
                cookies.Add((name, value));
            }
        }

        return cookies;
    }
}

