namespace ArcaDownloader.Core.Auth;

public sealed record PersistedCookie(
    string Name,
    string Value,
    string Domain,
    string Path,
    DateTimeOffset? Expires,
    bool Secure,
    bool HttpOnly);

