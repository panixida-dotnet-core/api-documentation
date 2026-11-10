using System.Text.Json.Serialization;

namespace PANiXiDA.Core.ApiDocumentation.AotSmoke;

internal sealed record StateChanged(Guid Id, string? Status);

[JsonSerializable(typeof(StateChanged))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class SmokeJsonContext : JsonSerializerContext;
