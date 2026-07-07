using System.Text.Json.Serialization;

namespace ArcaDownloader.Core.Auth;

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(List<PersistedCookie>))]
internal sealed partial class CookieJsonContext : JsonSerializerContext
{
}
