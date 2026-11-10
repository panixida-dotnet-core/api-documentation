using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace PANiXiDA.Core.ApiDocumentation.AsyncApi;

/// <summary>Builds AsyncAPI 3.0 documents from transport-owned metadata.</summary>
public static class AsyncApiDocumentBuilder
{
    private const string Messages = "messages";

    /// <summary>Builds a deterministic document with shared message components and optional replies.</summary>
    /// <param name="descriptor">The document identity.</param>
    /// <param name="operations">The operations belonging to this module and version.</param>
    /// <param name="servers">Optional configured servers.</param>
    /// <returns>A complete AsyncAPI document.</returns>
    public static JsonObject Build(ApiDocumentDescriptor descriptor,
        IEnumerable<AsyncApiOperation> operations, IEnumerable<AsyncApiServer>? servers = null)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(operations);
        if (descriptor.Format != ApiDocumentFormat.AsyncApi)
        {
            throw new ArgumentException("An AsyncAPI document descriptor is required.", nameof(descriptor));
        }

        var rendering = new RenderingContext(CreateServers(servers));
        var operationNodes = new JsonObject();
        var requiresBearer = false;
        foreach (var operation in operations.OrderBy(operation => operation.Id, StringComparer.Ordinal))
        {
            if (operationNodes.ContainsKey(operation.Id))
            {
                throw new InvalidOperationException($"AsyncAPI operation '{operation.Id}' is registered more than once.");
            }

            operationNodes[operation.Id] = CreateOperation(operation, rendering);
            requiresBearer |= operation.RequiresBearer;
        }

        var components = new JsonObject { [Messages] = rendering.Messages };
        if (requiresBearer)
        {
            components["securitySchemes"] = new JsonObject
            {
                ["Bearer"] = new JsonObject { ["type"] = "http", ["scheme"] = "bearer" }
            };
        }

        var info = new JsonObject
        {
            ["title"] = descriptor.Title,
            ["version"] = descriptor.Version,
            ["x-module"] = descriptor.Module
        };
        if (descriptor.Deprecated)
        {
            info["x-deprecated"] = true;
        }

        return new JsonObject
        {
            ["asyncapi"] = "3.0.0",
            ["info"] = info,
            ["defaultContentType"] = "application/json",
            ["servers"] = rendering.Servers,
            ["channels"] = rendering.Channels,
            ["operations"] = operationNodes,
            ["components"] = components
        };
    }

    private static JsonObject CreateOperation(AsyncApiOperation operation, RenderingContext rendering)
    {
        var channelKey = operation.ServerName is null ? operation.Address : operation.ServerName + ":" + operation.Address;
        var channel = GetChannel(rendering, channelKey, operation.Address, operation.Protocol, operation.ServerName);
        AddMessage(rendering.Messages, operation.Message);
        var messageKey = ComponentKey(operation.Message.Name);
        channel[Messages]![messageKey] = Reference($"#/components/messages/{messageKey}");
        var node = new JsonObject
        {
            ["action"] = operation.Action == AsyncApiAction.Send ? "send" : "receive",
            ["title"] = operation.Id,
            ["channel"] = Reference($"#/channels/{EscapePointer(channelKey)}"),
            [Messages] = new JsonArray(Reference(
                $"#/channels/{EscapePointer(channelKey)}/messages/{messageKey}"))
        };
        if (operation.Summary is not null)
        {
            node["summary"] = operation.Summary;
        }

        if (operation.Bindings is { } bindings)
        {
            node["bindings"] = bindings;
        }

        if (operation.Reply is { } reply)
        {
            node["reply"] = CreateReply(operation, reply, rendering);
        }

        if (operation.RequiresBearer)
        {
            node["security"] = new JsonArray(Reference("#/components/securitySchemes/Bearer"));
        }

        return node;
    }

    private static JsonObject CreateReply(AsyncApiOperation operation, AsyncApiMessage reply, RenderingContext rendering)
    {
        AddMessage(rendering.Messages, reply);
        var messageKey = ComponentKey(reply.Name);
        var key = operation.Id + ".reply";
        if (rendering.Channels.ContainsKey(key))
        {
            throw new InvalidOperationException($"AsyncAPI reply channel '{key}' conflicts with another channel.");
        }

        var channel = GetChannel(rendering, key, operation.Address, operation.Protocol, operation.ServerName);
        channel[Messages]![messageKey] = Reference($"#/components/messages/{messageKey}");
        return new JsonObject
        {
            ["channel"] = Reference($"#/channels/{EscapePointer(key)}"),
            [Messages] = new JsonArray(Reference($"#/channels/{EscapePointer(key)}/messages/{messageKey}"))
        };
    }

    private static JsonObject CreateServers(IEnumerable<AsyncApiServer>? servers)
    {
        var result = new JsonObject();
        foreach (var server in (servers ?? []).OrderBy(server => server.Name, StringComparer.Ordinal))
        {
            if (result.ContainsKey(server.Name))
            {
                throw new InvalidOperationException($"AsyncAPI server '{server.Name}' is registered more than once.");
            }

            result[server.Name] = new JsonObject { ["host"] = server.Host, ["protocol"] = server.Protocol };
        }

        return result;
    }

    private static JsonObject GetChannel(RenderingContext rendering, string key, string address, string protocol, string? serverName)
    {
        var identity = new ChannelIdentity(address, protocol, serverName);
        if (rendering.ChannelIdentities.TryGetValue(key, out var existing))
        {
            if (existing != identity)
            {
                throw new InvalidOperationException($"AsyncAPI channel '{key}' has conflicting address, protocol or server metadata.");
            }

            return (JsonObject)rendering.Channels[key]!;
        }

        var serverRefs = new JsonArray();
        foreach (var server in rendering.Servers.Where(server => server.Value!["protocol"]!.GetValue<string>() == protocol &&
                     (serverName is null || server.Key == serverName)))
        {
            serverRefs.Add((JsonNode)Reference($"#/servers/{EscapePointer(server.Key)}"));
        }

        if (serverName is not null && serverRefs.Count == 0)
        {
            throw new InvalidOperationException($"AsyncAPI server '{serverName}' with protocol '{protocol}' was not registered.");
        }

        var channel = new JsonObject { ["address"] = address, [Messages] = new JsonObject() };
        if (serverRefs.Count > 0)
        {
            channel["servers"] = serverRefs;
        }

        rendering.Channels[key] = channel;
        rendering.ChannelIdentities.Add(key, identity);
        return channel;
    }

    private static void AddMessage(JsonObject messages, AsyncApiMessage message)
    {
        var payload = message.Schema;
        var key = ComponentKey(message.Name);
        JsonSchemaReferences.Rebase(payload, $"#/components/messages/{key}/payload/schema");
        var node = new JsonObject
        {
            ["name"] = message.Name,
            ["payload"] = new JsonObject { ["schemaFormat"] = "application/schema+json;version=draft-2020-12", ["schema"] = payload }
        };
        if (message.Summary is not null)
        {
            node["summary"] = message.Summary;
        }

        if (messages[key] is { } existing && !JsonNode.DeepEquals(existing, node))
        {
            throw new InvalidOperationException($"AsyncAPI message '{message.Name}' has conflicting definitions.");
        }

        messages[key] = node;
    }

    private static JsonObject Reference(string path) => new() { ["$ref"] = path };

    private static string EscapePointer(string value) =>
        Uri.EscapeDataString(value.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal));

    private static string ComponentKey(string name)
    {
        var result = new StringBuilder();
        foreach (var character in name)
        {
            if (char.IsAsciiLetterOrDigit(character) || character is '.' or '-')
            {
                result.Append(character);
            }
            else
            {
                result.Append('_').Append(((int)character).ToString("X4", CultureInfo.InvariantCulture));
            }
        }

        return result.ToString();
    }

    private sealed record ChannelIdentity(string Address, string Protocol, string? Server);

    private sealed class RenderingContext(JsonObject servers)
    {
        internal JsonObject Servers { get; } = servers;
        internal JsonObject Channels { get; } = new();
        internal JsonObject Messages { get; } = new();
        internal Dictionary<string, ChannelIdentity> ChannelIdentities { get; } = new(StringComparer.Ordinal);
    }
}
