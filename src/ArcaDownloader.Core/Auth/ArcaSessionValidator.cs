using System.Net;
using ArcaDownloader.Core.Download;

namespace ArcaDownloader.Core.Auth;

public static class ArcaSessionValidator
{
    private static readonly Uri ProfileUri = new("https://arca.live/settings/profile");

    public static async Task<bool> HasValidSessionAsync(CookieContainer cookies, CancellationToken cancellationToken = default)
    {
        var result = await CheckSessionAsync(cookies, cancellationToken);
        return result.IsValid;
    }

    public static async Task<ArcaSessionCheckResult> CheckSessionAsync(CookieContainer cookies, CancellationToken cancellationToken = default)
    {
        using var client = HttpClientFactory.Create(ProfileUri, cookies);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        using var response = await client.GetAsync(ProfileUri, linked.Token);
        var html = await response.Content.ReadAsStringAsync(linked.Token);
        var statusCode = (int)response.StatusCode;
        var finalUri = response.RequestMessage?.RequestUri;
        var hasForbiddenMarker = html.Contains("ERROR 403", StringComparison.OrdinalIgnoreCase)
                                 || html.Contains("권한이 없습니다.", StringComparison.OrdinalIgnoreCase);
        var isProfileUri = finalUri is not null
                           && finalUri.Host.EndsWith("arca.live", StringComparison.OrdinalIgnoreCase)
                           && finalUri.AbsolutePath.Contains("/settings/profile", StringComparison.OrdinalIgnoreCase);

        if (statusCode is 401 or 403 or 451)
        {
            return new ArcaSessionCheckResult(false, statusCode, finalUri?.ToString(), hasForbiddenMarker, isProfileUri, $"HTTP {statusCode}");
        }

        if (hasForbiddenMarker)
        {
            return new ArcaSessionCheckResult(false, statusCode, finalUri?.ToString(), true, isProfileUri, "Profile page contains forbidden marker");
        }

        if (!isProfileUri)
        {
            return new ArcaSessionCheckResult(false, statusCode, finalUri?.ToString(), false, false, "Profile request did not end on /settings/profile");
        }

        return new ArcaSessionCheckResult(true, statusCode, finalUri?.ToString(), false, true, "OK");
    }
}

public sealed record ArcaSessionCheckResult(
    bool IsValid,
    int StatusCode,
    string? FinalUri,
    bool HasForbiddenMarker,
    bool IsProfileUri,
    string Reason);
