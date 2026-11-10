using System.Text.Json.Nodes;

namespace PANiXiDA.Core.ApiDocumentation.AsyncApi;

/// <summary>Builds AsyncAPI 3.0 documents from transport-owned metadata.</summary>
public static class AsyncApiDocumentBuilder
{
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

        var channels = new JsonObject();
        var operationNodes = new JsonObject();
        var messages = new JsonObject();
        var serverNodes = CreateServers(servers);
        var requiresBearer = false;
        foreach (var operation in operations.OrderBy(operation => operation.Id, StringComparer.Ordinal))
        {
            if (operationNodes.ContainsKey(operation.Id))
            {
                throw new InvalidOperationException($"AsyncAPI operation '{operation.Id}' is registered more than once.");
            }

            var channelKey = operation.ServerName is null ? operation.Address : operation.ServerName + ":" + operation.Address;
            var channel = GetChannel(channels, channelKey, operation.Address, operation.Protocol, serverNodes, operation.ServerName);
            AddMessage(messages, operation.Message);
            var channelMessages = (JsonObject)channel["messages"]!;
            channelMessages[operation.Message.Name] = Reference($"#/components/messages/{EscapePointer(operation.Message.Name)}");
            var node = new JsonObject
            {
                ["action"] = operation.Action == AsyncApiAction.Send ? "send" : "receive",
                ["title"] = operation.Id,
                ["channel"] = Reference($"#/channels/{EscapePointer(channelKey)}"),
                ["messages"] = new JsonArray(Reference(
                    $"#/channels/{EscapePointer(channelKey)}/messages/{EscapePointer(operation.Message.Name)}"))
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
                AddMessage(messages, reply);
                var replyKey = operation.Id + ".reply";
                if (channels.ContainsKey(replyKey))
                {
                    throw new InvalidOperationException($"AsyncAPI reply channel '{replyKey}' conflicts with another channel.");
                }

                var replyChannel = GetChannel(channels, replyKey, operation.Address, operation.Protocol, serverNodes, operation.ServerName);
                replyChannel["messages"]![reply.Name] = Reference($"#/components/messages/{EscapePointer(reply.Name)}");
                node["reply"] = new JsonObject
                {
                    ["channel"] = Reference($"#/channels/{EscapePointer(replyKey)}"),
                    ["messages"] = new JsonArray(Reference(
                        $"#/channels/{EscapePointer(replyKey)}/messages/{EscapePointer(reply.Name)}"))
                };
            }

            if (operation.RequiresBearer)
            {
                requiresBearer = true;
                node["security"] = new JsonArray(Reference("#/components/securitySchemes/Bearer"));
            }

            operationNodes[operation.Id] = node;
        }

        var components = new JsonObject { ["messages"] = messages };
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
            ["servers"] = serverNodes,
            ["channels"] = channels,
            ["operations"] = operationNodes,
            ["components"] = components
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

    private static JsonObject GetChannel(JsonObject channels, string key, string address, string protocol,
        JsonObject servers, string? serverName)
    {
        if (channels[key] is JsonObject existing)
        {
            return existing;
        }

        var serverRefs = new JsonArray();
        foreach (var server in servers)
        {
            if (server.Value!["protocol"]!.GetValue<string>() == protocol && (serverName is null || server.Key == serverName))
            {
                serverRefs.Add((JsonNode)Reference($"#/servers/{EscapePointer(server.Key)}"));
            }
        }

        if (serverName is not null && serverRefs.Count == 0)
        {
            throw new InvalidOperationException($"AsyncAPI server '{serverName}' with protocol '{protocol}' was not registered.");
        }

        var channel = new JsonObject { ["address"] = address, ["messages"] = new JsonObject() };
        if (serverRefs.Count > 0)
        {
            channel["servers"] = serverRefs;
        }

        channels[key] = channel;
        return channel;
    }

    private static void AddMessage(JsonObject messages, AsyncApiMessage message)
    {
        var payload = message.Schema;
        RewriteReferences(payload, $"#/components/messages/{EscapePointer(message.Name)}/payload/schema");
        var node = new JsonObject
        {
            ["name"] = message.Name,
            ["payload"] = new JsonObject
            {
                ["schemaFormat"] = "application/schema+json;version=draft-2020-12",
                ["schema"] = payload
            }
        };
        if (message.Summary is not null)
        {
            node["summary"] = message.Summary;
        }

        if (messages[message.Name] is { } existing && !JsonNode.DeepEquals(existing, node))
        {
            throw new InvalidOperationException($"AsyncAPI message '{message.Name}' has conflicting definitions.");
        }

        messages[message.Name] = node;
    }

    private static void RewriteReferences(JsonNode node, string prefix)
    {
        if (node is JsonObject properties)
        {
            if (properties["$ref"] is JsonValue reference &&
                reference.TryGetValue<string>(out var value) && value.StartsWith('#'))
            {
                properties["$ref"] = prefix + value[1..];
            }

            foreach (var property in properties.ToArray())
            {
                if (property.Value is not null)
                {
                    RewriteReferences(property.Value, prefix);
                }
            }
        }
        else if (node is JsonArray items)
        {
            foreach (var item in items)
            {
                if (item is not null)
                {
                    RewriteReferences(item, prefix);
                }
            }
        }
    }

    private static JsonObject Reference(string path)
    {
        return new JsonObject { ["$ref"] = path };
    }

    private static string EscapePointer(string value)
    {
        return Uri.EscapeDataString(value.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal));
    }
}
