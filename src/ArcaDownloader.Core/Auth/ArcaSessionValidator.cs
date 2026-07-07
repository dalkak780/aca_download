using System.Net;
using ArcaDownloader.Core.Download;

namespace ArcaDownloader.Core.Auth;

public static class ArcaSessionValidator
{
    private static readonly Uri ProfileUri = new("https://arca.live/settings/profile");

    public static async Task<bool> HasValidSessionAsync(CookieContainer cookies, CancellationToken cancellationToken = default)
    {
        using var client = HttpClientFactory.Create(ProfileUri, cookies);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        using var response = await client.GetAsync(ProfileUri, linked.Token);
        var html = await response.Content.ReadAsStringAsync(linked.Token);

        if ((int)response.StatusCode is 401 or 403 or 451)
        {
            return false;
        }

        if (html.Contains("ERROR 403", StringComparison.OrdinalIgnoreCase)
            || html.Contains("권한이 없습니다.", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var finalUri = response.RequestMessage?.RequestUri;
        return finalUri is not null
               && finalUri.Host.EndsWith("arca.live", StringComparison.OrdinalIgnoreCase)
               && finalUri.AbsolutePath.Contains("/settings/profile", StringComparison.OrdinalIgnoreCase);
    }
}
