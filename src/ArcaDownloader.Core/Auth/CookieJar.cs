using System.Net;

namespace ArcaDownloader.Core.Auth;

public sealed class CookieJar
{
    public CookieJar(string path)
    {
        Path = path;
    }

    public string Path { get; }

    public static CookieJar Default()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return new CookieJar(System.IO.Path.Combine(root, "ArcaDownloader", "cookies.json"));
    }

    public async Task<CookieContainer> LoadAsync(CancellationToken cancellationToken = default)
    {
        var container = new CookieContainer();
        if (!File.Exists(Path)) return container;

        await using var stream = File.OpenRead(Path);
        var cookies = await System.Text.Json.JsonSerializer.DeserializeAsync(stream, CookieJsonContext.Default.ListPersistedCookie, cancellationToken)
                      ?? [];
        var now = DateTimeOffset.UtcNow;
        foreach (var item in cookies)
        {
            if (item.Expires is not null && item.Expires <= now) continue;

            container.Add(new Cookie(item.Name, item.Value, item.Path, NormalizeDomain(item.Domain))
            {
                Expires = item.Expires?.UtcDateTime ?? DateTime.MinValue,
                Secure = item.Secure,
                HttpOnly = item.HttpOnly
            });
        }

        return container;
    }

    public async Task SaveAsync(IEnumerable<PersistedCookie> cookies, CancellationToken cancellationToken = default)
    {
        var dir = System.IO.Path.GetDirectoryName(Path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var now = DateTimeOffset.UtcNow;
        var liveCookies = cookies
            .Where(cookie => cookie.Expires is null || cookie.Expires > now)
            .OrderBy(cookie => cookie.Domain, StringComparer.OrdinalIgnoreCase)
            .ThenBy(cookie => cookie.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        await using var stream = File.Create(Path);
        await System.Text.Json.JsonSerializer.SerializeAsync(stream, liveCookies, CookieJsonContext.Default.ListPersistedCookie, cancellationToken);
    }

    public async Task SaveFromHeaderAsync(string cookieHeader, CancellationToken cancellationToken = default)
    {
        var cookies = Services.CookieHeaderParser.Parse(cookieHeader)
            .Select(pair => new PersistedCookie(pair.Name, pair.Value, "arca.live", "/", null, true, false));
        await SaveAsync(cookies, cancellationToken);
    }

    public static IReadOnlyList<PersistedCookie> FromContainer(CookieContainer container, Uri uri)
    {
        return container.GetCookies(uri)
            .Cast<Cookie>()
            .Select(cookie => new PersistedCookie(
                cookie.Name,
                cookie.Value,
                string.IsNullOrWhiteSpace(cookie.Domain) ? uri.Host : cookie.Domain,
                string.IsNullOrWhiteSpace(cookie.Path) ? "/" : cookie.Path,
                cookie.Expires == DateTime.MinValue ? null : new DateTimeOffset(cookie.Expires.ToUniversalTime()),
                cookie.Secure,
                cookie.HttpOnly))
            .ToList();
    }

    private static string NormalizeDomain(string domain)
    {
        return string.IsNullOrWhiteSpace(domain) ? "arca.live" : domain.TrimStart('.');
    }
}
