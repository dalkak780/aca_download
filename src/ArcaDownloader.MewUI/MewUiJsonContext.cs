using System.Text.Json.Serialization;

[JsonSerializable(typeof(string))]
internal sealed partial class MewUiJsonContext : JsonSerializerContext
{
}
