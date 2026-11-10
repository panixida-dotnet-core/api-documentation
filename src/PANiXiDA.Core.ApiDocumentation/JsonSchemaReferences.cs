using System.Text.Json.Nodes;

namespace PANiXiDA.Core.ApiDocumentation;

internal static class JsonSchemaReferences
{
    private static readonly HashSet<string> Maps = new(StringComparer.Ordinal)
    {
        "properties", "patternProperties", "$defs", "definitions", "dependentSchemas", "dependencies"
    };
    private static readonly HashSet<string> Arrays = new(StringComparer.Ordinal)
    {
        "allOf", "anyOf", "oneOf", "prefixItems"
    };
    private static readonly HashSet<string> Schemas = new(StringComparer.Ordinal)
    {
        "not", "if", "then", "else", "contains", "propertyNames", "additionalProperties",
        "unevaluatedProperties", "items", "unevaluatedItems", "additionalItems", "contentSchema"
    };

    internal static void Rebase(JsonNode node, string prefix)
    {
        if (node is not JsonObject properties)
        {
            return;
        }

        if (properties["$ref"] is JsonValue reference && reference.TryGetValue<string>(out var value) &&
            (value == "#" || value.StartsWith("#/", StringComparison.Ordinal)))
        {
            properties["$ref"] = prefix + value[1..];
        }

        foreach (var child in properties.SelectMany(ChildSchemas).OfType<JsonNode>())
        {
            Rebase(child, prefix);
        }
    }

    private static IEnumerable<JsonNode?> ChildSchemas(KeyValuePair<string, JsonNode?> property)
    {
        if (Maps.Contains(property.Key))
        {
            return (property.Value as JsonObject)?.Select(item => item.Value) ?? [];
        }

        if (Arrays.Contains(property.Key) || property.Key == "items" && property.Value is JsonArray)
        {
            return (property.Value as JsonArray) ?? [];
        }

        return Schemas.Contains(property.Key) ? [property.Value] : [];
    }
}
