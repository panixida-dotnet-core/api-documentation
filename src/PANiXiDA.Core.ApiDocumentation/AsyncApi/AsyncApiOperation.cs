using System.Text.Json.Nodes;

namespace PANiXiDA.Core.ApiDocumentation.AsyncApi;

/// <summary>Describes a transport operation using AsyncAPI concepts.</summary>
public sealed class AsyncApiOperation
{
    private readonly JsonObject? _bindings;

    /// <summary>Creates an operation description.</summary>
    /// <param name="id">The operation's unique identifier.</param>
    /// <param name="address">The channel address, such as a hub path or Kafka topic.</param>
    /// <param name="action">The direction from the application's perspective.</param>
    /// <param name="message">The application message.</param>
    /// <param name="protocol">The server protocol.</param>
    /// <param name="summary">The optional operation summary.</param>
    /// <param name="bindings">Optional protocol-specific operation bindings.</param>
    /// <param name="reply">The optional reply or stream-item message.</param>
    /// <param name="requiresBearer">Whether the operation requires bearer authentication.</param>
    /// <param name="serverName">An optional server name isolating channels on named brokers.</param>
    public AsyncApiOperation(string id, string address, AsyncApiAction action,
        AsyncApiMessage message, string protocol, string? summary = null,
        JsonObject? bindings = null, AsyncApiMessage? reply = null, bool requiresBearer = false,
        string? serverName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(address);
        ArgumentNullException.ThrowIfNull(message);
        ArgumentException.ThrowIfNullOrWhiteSpace(protocol);
        if (!Enum.IsDefined(action))
        {
            throw new ArgumentOutOfRangeException(nameof(action));
        }

        Id = id;
        Address = address;
        Action = action;
        Message = message;
        Protocol = protocol;
        Summary = summary;
        _bindings = (JsonObject?)bindings?.DeepClone();
        Reply = reply;
        RequiresBearer = requiresBearer;
        ServerName = serverName;
    }

    /// <summary>Gets the operation identifier.</summary>
    public string Id { get; }
    /// <summary>Gets the channel address.</summary>
    public string Address { get; }
    /// <summary>Gets the message direction.</summary>
    public AsyncApiAction Action { get; }
    /// <summary>Gets the application message.</summary>
    public AsyncApiMessage Message { get; }
    /// <summary>Gets the server protocol.</summary>
    public string Protocol { get; }
    /// <summary>Gets the operation summary.</summary>
    public string? Summary { get; }
    /// <summary>Gets an independent copy of the protocol bindings.</summary>
    public JsonObject? Bindings => (JsonObject?)_bindings?.DeepClone();
    /// <summary>Gets the optional reply message.</summary>
    public AsyncApiMessage? Reply { get; }
    /// <summary>Gets whether bearer authentication is required.</summary>
    public bool RequiresBearer { get; }
    /// <summary>Gets the optional server name limiting this operation to one broker.</summary>
    public string? ServerName { get; }
}
