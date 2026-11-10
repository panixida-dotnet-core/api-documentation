using System.Text.Json.Nodes;

namespace PANiXiDA.Core.ApiDocumentation.AsyncApi;

/// <summary>Describes an application message and its serialization schema.</summary>
public sealed class AsyncApiMessage
{
    private readonly JsonNode _schema;

    /// <summary>Creates a message description.</summary>
    /// <param name="name">The stable component name.</param>
    /// <param name="schema">The payload JSON Schema.</param>
    /// <param name="summary">The optional message summary.</param>
    public AsyncApiMessage(string name, JsonNode schema, string? summary = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(schema);
        Name = name;
        _schema = schema.DeepClone();
        Summary = summary;
    }

    /// <summary>Gets the stable message name.</summary>
    public string Name { get; }
    /// <summary>Gets an independent copy of the payload schema.</summary>
    public JsonNode Schema => _schema.DeepClone();
    /// <summary>Gets the message summary.</summary>
    public string? Summary { get; }
}
