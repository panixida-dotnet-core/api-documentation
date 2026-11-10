using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace PANiXiDA.Core.ApiDocumentation;

/// <summary>Exports schemas from explicitly registered source-generated JSON metadata.</summary>
public sealed class ApiSchemaRegistry
{
    private readonly JsonSerializerContext[] _contexts;

    /// <summary>Creates a registry of application JSON contexts.</summary>
    /// <param name="contexts">Contexts covering documented payload types.</param>
    public ApiSchemaRegistry(IEnumerable<JsonSerializerContext> contexts)
    {
        ArgumentNullException.ThrowIfNull(contexts);
        _contexts = [.. contexts];
        if (_contexts.Any(context => context is null))
        {
            throw new ArgumentException("JSON contexts must not contain null.", nameof(contexts));
        }
    }

    /// <summary>Gets a schema without using reflection-based serializer metadata.</summary>
    /// <param name="type">The documented payload type.</param>
    /// <param name="referencePrefix">The JSON pointer where the schema will be embedded in its containing payload.</param>
    /// <returns>The JSON Schema describing the registered serialization contract.</returns>
    public JsonNode GetSchema(Type type, string referencePrefix = "#")
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentException.ThrowIfNullOrWhiteSpace(referencePrefix);
        if (referencePrefix != "#" && !referencePrefix.StartsWith("#/", StringComparison.Ordinal))
        {
            throw new ArgumentException("Schema reference prefixes must be local JSON pointers.", nameof(referencePrefix));
        }
        foreach (var context in _contexts)
        {
            if (context.GetTypeInfo(type) is JsonTypeInfo typeInfo)
            {
                var schema = typeInfo.GetJsonSchemaAsNode();
                JsonSchemaReferences.Rebase(schema, referencePrefix);
                return schema;
            }
        }

        throw new InvalidOperationException(
            $"Source-generated JSON metadata was not registered for '{type.FullName}'. " +
            "Register a JsonSerializerContext containing the payload with AddApiDocumentationSchemas.");
    }
}
