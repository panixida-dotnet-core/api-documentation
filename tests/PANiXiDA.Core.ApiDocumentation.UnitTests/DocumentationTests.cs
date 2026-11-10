using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

using Microsoft.Extensions.DependencyInjection;

using PANiXiDA.Core.ApiDocumentation.AsyncApi;
using PANiXiDA.Core.ApiDocumentation.DependencyInjection;

namespace PANiXiDA.Core.ApiDocumentation.UnitTests;

public sealed class DocumentationTests
{
    [Fact(DisplayName = "Document names preserve non-default versions and normalize module casing")]
    public void NormalizesDocumentNames()
    {
        ApiDocumentNames.Create("Matchmaking", "SignalR", "1.0.0").ShouldBe("matchmaking-signalr-v1");
        ApiDocumentNames.Create("matchmaking", "kafka", "1.2").ShouldBe("matchmaking-kafka-v1.2");
        Should.Throw<ArgumentException>(() => ApiDocumentNames.Create("", "signalr", "1.0"));
        Should.Throw<ArgumentException>(() => ApiDocumentNames.Create("module", "", "1.0"));
        Should.Throw<ArgumentException>(() => ApiDocumentNames.Create("module", "signalr", ""));
        Should.Throw<ArgumentException>(() => ApiDocumentNames.Create("bad/path", "signalr", "1.0"));
    }

    [Fact(DisplayName = "Document descriptors reject invalid identities and routes")]
    public void DescriptorRejectsInvalidValues()
    {
        foreach (var name in new[] { "", " ", "module/path", "module?name", "кириллица" })
        {
            Should.Throw<ArgumentException>(() => new ApiDocumentDescriptor(name, "Title", "module", "1.0", ApiDocumentFormat.AsyncApi));
        }

        Should.Throw<ArgumentException>(() => new ApiDocumentDescriptor("name", "", "module", "1.0", ApiDocumentFormat.AsyncApi));
        Should.Throw<ArgumentException>(() => new ApiDocumentDescriptor("name", "Title", "", "1.0", ApiDocumentFormat.AsyncApi));
        Should.Throw<ArgumentException>(() => new ApiDocumentDescriptor("name", "Title", "module", "", ApiDocumentFormat.AsyncApi));
        Should.Throw<ArgumentOutOfRangeException>(() => new ApiDocumentDescriptor("name", "Title", "module", "1.0", default));
        Should.Throw<ArgumentOutOfRangeException>(() => new ApiDocumentDescriptor("name", "Title", "module", "1.0", (ApiDocumentFormat)99));
        foreach (var path in new[] { "relative", "//host", "/../secret", "/bad path", "/bad\npath", "/{name}", "/a?x", "/a#x", "/a\\b" })
        {
            Should.Throw<ArgumentException>(() => new ApiDocumentDescriptor("name", "Title", "module", "1.0", ApiDocumentFormat.AsyncApi, path));
        }

        var descriptor = new ApiDocumentDescriptor("module-v1", "Module", "module", "1.0", ApiDocumentFormat.OpenApi);
        descriptor.Path.ShouldBe("/openapi/module-v1.json");
        descriptor.Format.ShouldBe(ApiDocumentFormat.OpenApi);
        descriptor.Deprecated.ShouldBeFalse();
        new ApiDocumentDescriptor("module-v1", "Module", "module", "1.0", ApiDocumentFormat.AsyncApi, "/contracts/module.json").Path.ShouldBe("/contracts/module.json");
    }

    [Fact(DisplayName = "Catalog orders modules and rejects duplicate names and paths")]
    public void CatalogValidatesSources()
    {
        var first = new ApiDocumentDescriptor("z-v1", "Z", "z", "1.0", ApiDocumentFormat.AsyncApi);
        var second = new ApiDocumentDescriptor("a-v2", "A", "a", "2.0", ApiDocumentFormat.OpenApi);
        var catalog = new ApiDocumentCatalog([new Source(first, second)]);
        catalog.GetDocuments().Select(document => document.Name).ShouldBe(["a-v2", "z-v1"]);
        Should.Throw<ArgumentNullException>(() => new ApiDocumentCatalog(null!));
        Should.Throw<ArgumentException>(() => new ApiDocumentCatalog([null!]));
        Should.Throw<InvalidOperationException>(() => new ApiDocumentCatalog([new Source(first, first)]).GetDocuments());
        var duplicatePath = new ApiDocumentDescriptor("another", "Other", "a", "1.0", ApiDocumentFormat.AsyncApi, first.Path);
        Should.Throw<InvalidOperationException>(() => new ApiDocumentCatalog([new Source(first, duplicatePath)]).GetDocuments());
    }

    [Fact(DisplayName = "Schema registry uses generated metadata and does not fall back to reflection")]
    public void SchemasRequireGeneratedMetadata()
    {
        var registry = new ApiSchemaRegistry([ContractJsonContext.Default]);
        var schema = registry.GetSchema(typeof(Payload));
        schema["properties"]!["id"]!["type"]!.GetValue<string>().ShouldBe("string");
        Should.Throw<InvalidOperationException>(() => registry.GetSchema(typeof(Uri)));
        Should.Throw<ArgumentNullException>(() => registry.GetSchema(null!));
        Should.Throw<ArgumentNullException>(() => new ApiSchemaRegistry(null!));
        Should.Throw<ArgumentException>(() => new ApiSchemaRegistry([null!]));
    }

    [Fact(DisplayName = "Common registrations are idempotent and contexts are shared")]
    public void RegistersCommonServices()
    {
        var services = new ServiceCollection();
        services.AddApiDocumentSource<EmptySource>().AddApiDocumentSource<EmptySource>();
        services.AddApiDocumentationSchemas(ContractJsonContext.Default);
        using var provider = services.BuildServiceProvider();
        provider.GetServices<IApiDocumentSource>().Count().ShouldBe(1);
        provider.GetRequiredService<ApiDocumentCatalog>().GetDocuments().ShouldBeEmpty();
        provider.GetRequiredService<ApiSchemaRegistry>().GetSchema(typeof(Payload)).ShouldNotBeNull();
        Should.Throw<ArgumentNullException>(() => ServiceCollectionExtensions.AddApiDocumentSource<EmptySource>(null!));
        Should.Throw<ArgumentNullException>(() => ServiceCollectionExtensions.AddApiDocumentationSchemas(null!));
        Should.Throw<ArgumentNullException>(() => services.AddApiDocumentationSchemas(null!));
        Should.Throw<ArgumentException>(() => services.AddApiDocumentationSchemas([null!]));
    }

    [Fact(DisplayName = "AsyncAPI documents preserve module version direction replies and bearer requirements")]
    public void BuildsCompleteDocument()
    {
        var descriptor = new ApiDocumentDescriptor("module-signalr-v2", "Module", "module", "2.0", ApiDocumentFormat.AsyncApi, deprecated: true);
        var schema = new ApiSchemaRegistry([ContractJsonContext.Default]).GetSchema(typeof(Payload));
        var message = new AsyncApiMessage("StateChanged", schema, "State update");
        var bindings = new JsonObject { ["x-signalr"] = new JsonObject { ["target"] = "GetState" } };
        var operation = new AsyncApiOperation("GetState", "/hubs/state", AsyncApiAction.Receive, message, "signalr",
            "Read state")
        { Name = "Get current state", Bindings = bindings, Reply = message, RequiresBearer = true };
        var document = AsyncApiDocumentBuilder.Build(descriptor, [operation],
            [new AsyncApiServer("signalr", "localhost:5000", "signalr"), new AsyncApiServer("kafka", "localhost:9092", "kafka")]);

        document["asyncapi"]!.GetValue<string>().ShouldBe("3.0.0");
        document["info"]!["version"]!.GetValue<string>().ShouldBe("2.0");
        document["info"]!["x-module"]!.GetValue<string>().ShouldBe("module");
        document["info"]!["x-deprecated"]!.GetValue<bool>().ShouldBeTrue();
        document["operations"]!["GetState"]!["action"]!.GetValue<string>().ShouldBe("receive");
        document["operations"]!["GetState"]!["title"]!.GetValue<string>().ShouldBe("Get current state");
        operation.Name.ShouldBe("Get current state");
        document["operations"]!["GetState"]!["reply"].ShouldNotBeNull();
        document["operations"]!["GetState"]!["security"]!.AsArray().Count.ShouldBe(1);
        document["channels"]!["/hubs/state"]!["servers"]!.AsArray().Count.ShouldBe(1);
        document["components"]!["securitySchemes"]!["Bearer"]!["scheme"]!.GetValue<string>().ShouldBe("bearer");
        document["components"]!["messages"]!["StateChanged"]!["payload"]!["schemaFormat"]!.GetValue<string>().ShouldContain("2020-12");

        bindings["x-signalr"]!["target"] = "Changed";
        operation.Bindings!["x-signalr"]!["target"]!.GetValue<string>().ShouldBe("GetState");
        schema["description"] = "Changed";
        message.Schema["description"].ShouldBeNull();
        message.Summary.ShouldBe("State update");
        operation.Action.ShouldBe(AsyncApiAction.Receive);
    }

    [Fact(DisplayName = "Requests and replies share one channel while preserving operation and message contracts")]
    public void ReusesOperationChannelsForReplies()
    {
        var descriptor = new ApiDocumentDescriptor("events", "Events", "module", "1.0", ApiDocumentFormat.AsyncApi);
        AsyncApiMessage[] messages =
        [
            new("read.request", new JsonObject { ["const"] = "read.request" }),
            new("read.response", new JsonObject { ["const"] = "read.response" }),
            new("watch.request", new JsonObject { ["const"] = "watch.request" }),
            new("watch.response", new JsonObject { ["const"] = "watch.response" })
        ];
        var read = new AsyncApiOperation("read", "/hubs/state", AsyncApiAction.Receive, messages[0], "ws") { Reply = messages[1] };
        var watch = new AsyncApiOperation("watch", "/hubs/state", AsyncApiAction.Receive, messages[2], "ws") { Reply = messages[3] };

        var document = AsyncApiDocumentBuilder.Build(descriptor, [read, watch]);

        document["channels"]!.AsObject().Select(channel => channel.Key).ShouldBe(["/hubs/state"]);
        document["operations"]!.AsObject().Select(operation => operation.Key).ShouldBe(["read", "watch"]);
        document["channels"]!["/hubs/state"]!["messages"]!.AsObject().Select(message => message.Key).Order()
            .ShouldBe(messages.Select(message => message.Name).Order());
        document["components"]!["messages"]!.AsObject().Select(message => message.Key).Order()
            .ShouldBe(messages.Select(message => message.Name).Order());
        foreach (var operation in new[] { read, watch })
        {
            var actual = document["operations"]![operation.Id]!;
            actual["channel"]!["$ref"]!.GetValue<string>().ShouldBe("#/channels/~1hubs~1state");
            JsonNode.DeepEquals(actual["channel"], actual["reply"]!["channel"]).ShouldBeTrue();
            actual["messages"]!.AsArray().Count.ShouldBe(1);
            actual["messages"]![0]!["$ref"]!.GetValue<string>()
                .ShouldBe($"#/channels/~1hubs~1state/messages/{operation.Message.Name}");
            actual["reply"]!["messages"]!.AsArray().Count.ShouldBe(1);
            actual["reply"]!["messages"]![0]!["$ref"]!.GetValue<string>()
                .ShouldBe($"#/channels/~1hubs~1state/messages/{operation.Reply!.Name}");
        }

        foreach (var message in messages)
        {
            var component = document["components"]!["messages"]![message.Name]!;
            component["name"]!.GetValue<string>().ShouldBe(message.Name);
            JsonNode.DeepEquals(component["payload"]!["schema"], message.Schema).ShouldBeTrue();
        }
    }

    [Theory(DisplayName = "Reply messages do not reserve channel addresses derived from operation identifiers")]
    [InlineData("a", "z")]
    [InlineData("z", "a")]
    public void ReplyIdentifiersDoNotConflictWithChannelAddresses(string ordinaryId, string replyId)
    {
        var descriptor = new ApiDocumentDescriptor("events", "Events", "module", "1.0", ApiDocumentFormat.AsyncApi);
        var message = new AsyncApiMessage("Event", new JsonObject());
        var ordinary = new AsyncApiOperation(ordinaryId, replyId + ".reply", AsyncApiAction.Send, message, "kafka");
        var request = new AsyncApiOperation(replyId, "topic", AsyncApiAction.Receive, message, "kafka") { Reply = message };

        var document = AsyncApiDocumentBuilder.Build(descriptor, [ordinary, request]);

        document["channels"]!.AsObject().Count.ShouldBe(2);
        document["channels"]![replyId + ".reply"]!["address"]!.GetValue<string>().ShouldBe(replyId + ".reply");
        document["channels"]!["topic"]!["address"]!.GetValue<string>().ShouldBe("topic");
        document["operations"]![replyId]!["reply"]!["channel"]!["$ref"]!.GetValue<string>().ShouldBe("#/channels/topic");
    }

    [Fact(DisplayName = "AsyncAPI components are reused and conflicting operations messages or servers fail")]
    public void RejectsConflictingDefinitions()
    {
        var descriptor = new ApiDocumentDescriptor("events", "Events", "module", "1.0", ApiDocumentFormat.AsyncApi);
        var message = new AsyncApiMessage("event", new JsonObject { ["type"] = "string" });
        var send = new AsyncApiOperation("send", "topic", AsyncApiAction.Send, message, "kafka");
        var receive = new AsyncApiOperation("receive", "topic", AsyncApiAction.Receive, message, "kafka");
        var document = AsyncApiDocumentBuilder.Build(descriptor, [send, receive]);
        document["components"]!["messages"]!.AsObject().Count.ShouldBe(1);
        document["operations"]!["send"]!["action"]!.GetValue<string>().ShouldBe("send");
        document["components"]!["securitySchemes"].ShouldBeNull();
        document["info"]!["x-deprecated"].ShouldBeNull();
        Should.Throw<InvalidOperationException>(() => AsyncApiDocumentBuilder.Build(descriptor, [send, send]));
        var conflicting = new AsyncApiOperation("other", "topic", AsyncApiAction.Send,
            new AsyncApiMessage("event", new JsonObject { ["type"] = "integer" }), "kafka");
        Should.Throw<InvalidOperationException>(() => AsyncApiDocumentBuilder.Build(descriptor, [send, conflicting]));
        var server = new AsyncApiServer("kafka", "localhost:9092", "kafka");
        Should.Throw<InvalidOperationException>(() => AsyncApiDocumentBuilder.Build(descriptor, [], [server, server]));
        Should.Throw<ArgumentNullException>(() => AsyncApiDocumentBuilder.Build(null!, []));
        Should.Throw<ArgumentNullException>(() => AsyncApiDocumentBuilder.Build(descriptor, null!));
        Should.Throw<ArgumentException>(() => AsyncApiDocumentBuilder.Build(
            new ApiDocumentDescriptor("http", "HTTP", "module", "1.0", ApiDocumentFormat.OpenApi), []));
    }

    [Theory(DisplayName = "Nested and recursive schema references remain rooted in their document component")]
    [InlineData("$ref")]
    [InlineData("$dynamicRef")]
    public void RewritesSchemaReferences(string referenceKeyword)
    {
        var schema = new JsonObject
        {
            [referenceKeyword] = "#",
            ["anyOf"] = new JsonArray(new JsonObject { [referenceKeyword] = "#/$defs/item" }, null,
                new JsonObject { [referenceKeyword] = "https://example.com/schema" }),
            ["$defs"] = new JsonObject { ["item"] = new JsonObject { ["type"] = "string" } }
        };
        var message = new AsyncApiMessage("item/~", schema);
        var operation = new AsyncApiOperation("send", "topic", AsyncApiAction.Send, message, "kafka");
        var document = AsyncApiDocumentBuilder.Build(
            new ApiDocumentDescriptor("events", "Events", "module", "1.0", ApiDocumentFormat.AsyncApi), [operation]);
        var payload = document["components"]!["messages"]!["item_002F_007E"]!["payload"]!["schema"]!;
        payload[referenceKeyword]!.GetValue<string>().ShouldBe("#/components/messages/item_002F_007E/payload/schema");
        payload["anyOf"]![0]![referenceKeyword]!.GetValue<string>().ShouldBe("#/components/messages/item_002F_007E/payload/schema/$defs/item");
        payload["anyOf"]![2]![referenceKeyword]!.GetValue<string>().ShouldBe("https://example.com/schema");
    }

    [Theory(DisplayName = "Channel server and reply references preserve legal URI fragment characters")]
    [InlineData("hub.GetState@/hubs/state", "hub.GetState@~1hubs~1state")]
    [InlineData("broker:events", "broker:events")]
    [InlineData("events!$&'()*+,;=?", "events!$&'()*+,;=?")]
    [InlineData("events/~", "events~1~0")]
    [InlineData("events #%", "events%20%23%25")]
    public void PreservesUriFragmentCharacters(string name, string pointer)
    {
        var descriptor = new ApiDocumentDescriptor("events", "Events", "module", "1.0", ApiDocumentFormat.AsyncApi);
        var message = new AsyncApiMessage("Event", new JsonObject());
        var operation = new AsyncApiOperation(name, name, AsyncApiAction.Send, message, "kafka")
        {
            ServerName = name,
            Reply = message
        };

        var document = AsyncApiDocumentBuilder.Build(descriptor, [operation], [new AsyncApiServer(name, "localhost:9092", "kafka")]);

        var actual = document["operations"]![name]!;
        actual["channel"]!["$ref"]!.GetValue<string>().ShouldBe($"#/channels/{pointer}:{pointer}");
        actual["reply"]!["channel"]!["$ref"]!.GetValue<string>().ShouldBe($"#/channels/{pointer}:{pointer}");
        actual["reply"]!["messages"]![0]!["$ref"]!.GetValue<string>()
            .ShouldBe($"#/channels/{pointer}:{pointer}/messages/Event");
        document["channels"]![name + ":" + name]!["servers"]![0]!["$ref"]!.GetValue<string>()
            .ShouldBe($"#/servers/{pointer}");
    }

    [Fact(DisplayName = "Message display titles preserve stable component names references and payload schemas")]
    public void SeparatesMessageTitlesFromNames()
    {
        var schema = new JsonObject { ["type"] = "string" };
        var message = new AsyncApiMessage("stable-message", schema, "Message description") { Title = "GetState request" };
        var operation = new AsyncApiOperation("stable-operation", "events", AsyncApiAction.Receive, message, "kafka");

        var document = AsyncApiDocumentBuilder.Build(new("events", "Events", "module", "1.0", ApiDocumentFormat.AsyncApi), [operation]);

        var component = document["components"]!["messages"]!["stable-message"]!;
        component["name"]!.GetValue<string>().ShouldBe("stable-message");
        component["title"]!.GetValue<string>().ShouldBe("GetState request");
        component["summary"]!.GetValue<string>().ShouldBe("Message description");
        JsonNode.DeepEquals(component["payload"]!["schema"], schema).ShouldBeTrue();
        document["channels"]!["events"]!["messages"]!["stable-message"]!["$ref"]!.GetValue<string>()
            .ShouldBe("#/components/messages/stable-message");
        message.Title.ShouldBe("GetState request");
        new AsyncApiMessage("untitled", schema) { Title = null }.Title.ShouldBeNull();
        Should.Throw<ArgumentException>(() => new AsyncApiMessage("invalid", schema) { Title = " " });
        var conflicting = new AsyncApiOperation("other", "events", AsyncApiAction.Send,
            new AsyncApiMessage(message.Name, schema, message.Summary) { Title = "Another title" }, "kafka");
        Should.Throw<InvalidOperationException>(() => AsyncApiDocumentBuilder.Build(
            new("events", "Events", "module", "1.0", ApiDocumentFormat.AsyncApi), [operation, conflicting]));
    }

    [Fact(DisplayName = "Public metadata rejects missing required values")]
    public void MetadataGuards()
    {
        var message = new AsyncApiMessage("event", new JsonObject());
        var metadata = new ApiOperationMetadata("Publish event", "Event description");
        metadata.Name.ShouldBe("Publish event");
        metadata.Summary.ShouldBe("Event description");
        new ApiOperationMetadata("Publish event").Summary.ShouldBeNull();
        new AsyncApiOperation("id", "topic", AsyncApiAction.Send, message, "kafka") { Name = null }.Name.ShouldBeNull();
        new AsyncApiOperation("empty", "topic", AsyncApiAction.Send, message, "kafka") { Bindings = null }.Bindings.ShouldBeNull();
        Should.Throw<ArgumentException>(() => new AsyncApiMessage("", new JsonObject()));
        Should.Throw<ArgumentNullException>(() => new AsyncApiMessage("name", null!));
        Should.Throw<ArgumentException>(() => new AsyncApiOperation("", "topic", AsyncApiAction.Send, message, "kafka"));
        Should.Throw<ArgumentException>(() => new AsyncApiOperation("id", "", AsyncApiAction.Send, message, "kafka"));
        Should.Throw<ArgumentNullException>(() => new AsyncApiOperation("id", "topic", AsyncApiAction.Send, null!, "kafka"));
        Should.Throw<ArgumentException>(() => new AsyncApiOperation("id", "topic", AsyncApiAction.Send, message, ""));
        Should.Throw<ArgumentOutOfRangeException>(() => new AsyncApiOperation("id", "topic", default, message, "kafka"));
        Should.Throw<ArgumentOutOfRangeException>(() => new AsyncApiOperation("id", "topic", (AsyncApiAction)99, message, "kafka"));
        Should.Throw<ArgumentException>(() => new AsyncApiServer("", "host", "kafka"));
        Should.Throw<ArgumentException>(() => new AsyncApiServer("name", "", "kafka"));
        Should.Throw<ArgumentException>(() => new AsyncApiServer("name", "host", ""));
        Should.Throw<ArgumentException>(() => new AsyncApiServer("name", "user@host", "kafka"));
        Should.Throw<ArgumentException>(() => new AsyncApiServer("name", "bad\nhost", "kafka"));
        Should.Throw<ArgumentException>(() => new ApiOperationMetadata(""));
        Should.Throw<ArgumentException>(() => new AsyncApiOperation("id", "topic", AsyncApiAction.Send, message, "kafka") { Name = " " });
    }

    [Fact(DisplayName = "Named brokers isolate identical topic addresses and reject unknown servers")]
    public void IsolatesNamedBrokers()
    {
        var descriptor = new ApiDocumentDescriptor("events", "Events", "module", "1.0", ApiDocumentFormat.AsyncApi);
        var message = new AsyncApiMessage("event", new JsonObject { ["type"] = "string" });
        var first = new AsyncApiOperation("first", "events", AsyncApiAction.Send, message, "kafka") { ServerName = "primary" };
        var second = new AsyncApiOperation("second", "events", AsyncApiAction.Receive, message, "kafka") { ServerName = "external" };
        var document = AsyncApiDocumentBuilder.Build(descriptor, [first, second],
            [new AsyncApiServer("primary", "primary:9092", "kafka"), new AsyncApiServer("external", "external:9092", "kafka")]);
        document["channels"]!.AsObject().Count.ShouldBe(2);
        document["channels"]!["primary:events"]!["address"]!.GetValue<string>().ShouldBe("events");
        document["channels"]!["external:events"]!["servers"]![0]!["$ref"]!.GetValue<string>().ShouldBe("#/servers/external");
        Should.Throw<InvalidOperationException>(() => AsyncApiDocumentBuilder.Build(descriptor, [first]));
        first.ServerName.ShouldBe("primary");
    }

    [Fact(DisplayName = "Channels reject configured servers with incompatible protocols")]
    public void RejectsUnmatchedServerProtocols()
    {
        var descriptor = new ApiDocumentDescriptor("events", "Events", "module", "1.0", ApiDocumentFormat.AsyncApi);
        var message = new AsyncApiMessage("Event", new JsonObject());
        var operation = new AsyncApiOperation("send", "events", AsyncApiAction.Send, message, "kafka");
        var namedOperation = new AsyncApiOperation("send", "events", AsyncApiAction.Send, message, "kafka") { ServerName = "broker" };
        var server = new AsyncApiServer("broker", "localhost:5672", "amqp");

        var exception = Should.Throw<InvalidOperationException>(() => AsyncApiDocumentBuilder.Build(descriptor, [operation], [server]));

        exception.Message.ShouldBe("No AsyncAPI server with protocol 'kafka' was registered.");
        Should.Throw<InvalidOperationException>(() => AsyncApiDocumentBuilder.Build(descriptor, [namedOperation], [server]));
    }

    [Theory(DisplayName = "Channels and replies support omitted or empty server lists")]
    [InlineData(false)]
    [InlineData(true)]
    public void AllowsServerlessChannels(bool explicitEmptyServers)
    {
        var descriptor = new ApiDocumentDescriptor("events", "Events", "module", "1.0", ApiDocumentFormat.AsyncApi);
        var message = new AsyncApiMessage("Event", new JsonObject());
        var operation = new AsyncApiOperation("send", "events", AsyncApiAction.Send, message, "kafka") { Reply = message };

        var document = AsyncApiDocumentBuilder.Build(descriptor, [operation], explicitEmptyServers ? [] : null);

        document["servers"]!.AsObject().ShouldBeEmpty();
        document["channels"]!.AsObject().Count.ShouldBe(1);
        document["channels"]!["events"]!["servers"].ShouldBeNull();
        document["channels"]!["send.reply"].ShouldBeNull();
    }

    [Fact(DisplayName = "Channels and replies reference every matching server and exclude other protocols")]
    public void SelectsServersMatchingChannelProtocol()
    {
        var descriptor = new ApiDocumentDescriptor("events", "Events", "module", "1.0", ApiDocumentFormat.AsyncApi);
        var message = new AsyncApiMessage("Event", new JsonObject());
        var operation = new AsyncApiOperation("send", "events", AsyncApiAction.Send, message, "kafka") { Reply = message };
        AsyncApiServer[] servers =
        [
            new("primary", "primary:9092", "kafka"),
            new("secondary", "secondary:9092", "kafka"),
            new("other", "other:5672", "amqp")
        ];

        var document = AsyncApiDocumentBuilder.Build(descriptor, [operation], servers);

        foreach (var channel in document["channels"]!.AsObject())
        {
            channel.Value!["servers"]!.AsArray().Select(server => server!["$ref"]!.GetValue<string>())
                .ShouldBe(["#/servers/primary", "#/servers/secondary"]);
        }
    }

    [Fact(DisplayName = "Embedded recursive payload schemas keep references inside their own schema")]
    public void RebasesEmbeddedSchemas()
    {
        var registry = new ApiSchemaRegistry([ContractJsonContext.Default]);
        var schema = registry.GetSchema(typeof(RecursivePayload), "#/properties/arguments/prefixItems/0");
        schema.ToJsonString().ShouldContain("#/properties/arguments/prefixItems/0");
        Should.Throw<ArgumentException>(() => registry.GetSchema(typeof(Payload), ""));
        Should.Throw<ArgumentException>(() => registry.GetSchema(typeof(Payload), "external"));
    }

    [Fact(DisplayName = "Channels reject incompatible protocols")]
    public void RejectsIncompatibleChannelReuse()
    {
        var descriptor = new ApiDocumentDescriptor("events", "Events", "module", "1.0", ApiDocumentFormat.AsyncApi);
        var message = new AsyncApiMessage("Event", new JsonObject());
        var send = new AsyncApiOperation("a", "events", AsyncApiAction.Send, message, "kafka") { Reply = message };
        var otherProtocol = new AsyncApiOperation("z", "events", AsyncApiAction.Send, message, "amqp");
        Should.Throw<InvalidOperationException>(() => AsyncApiDocumentBuilder.Build(descriptor, [send, otherProtocol]));
    }

    [Fact(DisplayName = "Message component identifiers are valid and collision-free for arbitrary display names")]
    public void EncodesMessageComponentKeys()
    {
        string[] names = ["a.b-c", "a_b", "a/b", "a_002Fb", "Event<T>", "Событие", "event name"];
        var operations = names.Select((name, index) => new AsyncApiOperation("send" + index, "topic", AsyncApiAction.Send,
            new AsyncApiMessage(name, new JsonObject()), "kafka"));
        var document = AsyncApiDocumentBuilder.Build(new("events", "Events", "module", "1.0", ApiDocumentFormat.AsyncApi), operations);
        var messages = document["components"]!["messages"]!.AsObject();
        messages.Count.ShouldBe(names.Length);
        messages.All(message => message.Key.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_')).ShouldBeTrue();
        messages.Select(message => message.Value!["name"]!.GetValue<string>()).Order().ShouldBe(names.Order());
    }

    [Theory(DisplayName = "Reference rebasing traverses schema keywords while preserving literal instance values")]
    [InlineData("properties", "map")]
    [InlineData("patternProperties", "map")]
    [InlineData("$defs", "map")]
    [InlineData("definitions", "map")]
    [InlineData("dependentSchemas", "map")]
    [InlineData("dependencies", "map")]
    [InlineData("allOf", "array")]
    [InlineData("anyOf", "array")]
    [InlineData("oneOf", "array")]
    [InlineData("prefixItems", "array")]
    [InlineData("items", "array")]
    [InlineData("not", "single")]
    [InlineData("if", "single")]
    [InlineData("then", "single")]
    [InlineData("else", "single")]
    [InlineData("contains", "single")]
    [InlineData("propertyNames", "single")]
    [InlineData("additionalProperties", "single")]
    [InlineData("unevaluatedProperties", "single")]
    [InlineData("items", "single")]
    [InlineData("unevaluatedItems", "single")]
    [InlineData("additionalItems", "single")]
    [InlineData("contentSchema", "single")]
    public void PreservesLiteralReferences(string keyword, string kind)
    {
        var reference = new JsonObject { ["$ref"] = "#" };
        JsonNode child = kind switch
        {
            "map" => new JsonObject { ["value"] = reference },
            "array" => new JsonArray(reference, null, JsonValue.Create(true)),
            _ => reference
        };
        var schema = new JsonObject
        {
            [keyword] = child,
            ["const"] = new JsonObject { ["$ref"] = "#original", ["$dynamicRef"] = "#/$defs/value" },
            ["default"] = new JsonObject { ["$ref"] = "#original" },
            ["enum"] = new JsonArray(new JsonObject { ["$ref"] = "#original" }),
            ["examples"] = new JsonArray(new JsonObject { ["$ref"] = "#original" })
        };
        var message = new AsyncApiMessage("Event", schema);
        var document = AsyncApiDocumentBuilder.Build(new("events", "Events", "module", "1.0", ApiDocumentFormat.AsyncApi),
            [new AsyncApiOperation("send", "topic", AsyncApiAction.Send, message, "kafka")]);
        var payload = document["components"]!["messages"]!["Event"]!["payload"]!["schema"]!;
        payload["const"]!["$ref"]!.GetValue<string>().ShouldBe("#original");
        payload["const"]!["$dynamicRef"]!.GetValue<string>().ShouldBe("#/$defs/value");
        payload["default"]!["$ref"]!.GetValue<string>().ShouldBe("#original");
        payload["enum"]![0]!["$ref"]!.GetValue<string>().ShouldBe("#original");
        payload["examples"]![0]!["$ref"]!.GetValue<string>().ShouldBe("#original");
        payload[keyword]!.ToJsonString().ShouldContain("#/components/messages/Event/payload/schema");
    }

    [Theory(DisplayName = "Identified schema resources preserve their local references at every nesting level")]
    [InlineData("https://example.com/event", false)]
    [InlineData("event", false)]
    [InlineData("https://example.com/event", true)]
    [InlineData("event", true)]
    public void PreservesIdentifiedSchemaReferences(string id, bool nested)
    {
        var resource = JsonNode.Parse("""
            {
              "$ref": "#/$defs/value",
              "$dynamicRef": "#/$defs/value",
              "$defs": {
                "value": { "type": "string" },
                "recursive": { "$ref": "#" },
                "child": {
                  "$id": "child",
                  "$ref": "#/$defs/value",
                  "$dynamicRef": "#/$defs/value",
                  "$defs": { "value": { "type": "integer" } }
                }
              },
              "properties": { "value": { "$ref": "#/$defs/value" } }
            }
            """)!;
        resource["$id"] = id;
        var schema = nested
            ? new JsonObject
            {
                ["$ref"] = "#/$defs/value",
                ["$defs"] = new JsonObject { ["value"] = new JsonObject { ["type"] = "string" } },
                ["properties"] = new JsonObject { ["event"] = resource.DeepClone() }
            }
            : resource.DeepClone();
        var operation = new AsyncApiOperation("send", "events", AsyncApiAction.Send,
            new AsyncApiMessage("Event", schema), "kafka");

        var document = AsyncApiDocumentBuilder.Build(new("events", "Events", "module", "1.0", ApiDocumentFormat.AsyncApi), [operation]);

        var payload = document["components"]!["messages"]!["Event"]!["payload"]!["schema"]!;
        var actualResource = nested ? payload["properties"]!["event"] : payload;
        JsonNode.DeepEquals(actualResource, resource).ShouldBeTrue();
        if (nested)
        {
            payload["$ref"]!.GetValue<string>().ShouldBe("#/components/messages/Event/payload/schema/$defs/value");
        }
    }

    [Fact(DisplayName = "Schema rebasing preserves anchors and safely ignores non-schema metadata")]
    public void PreservesAnchorsAndOpaqueMetadata()
    {
        var schema = new JsonObject
        {
            ["$ref"] = "#anchor",
            ["$dynamicRef"] = "#dynamic-anchor",
            ["properties"] = null,
            ["allOf"] = null,
            ["anyOf"] = new JsonArray(new JsonObject { ["$ref"] = false, ["$dynamicRef"] = false }),
            ["$defs"] = new JsonObject
            {
                ["value"] = new JsonObject { ["$anchor"] = "anchor", ["$dynamicAnchor"] = "dynamic-anchor", ["type"] = "string" }
            }
        };
        var message = new AsyncApiMessage("Event", schema);
        var document = AsyncApiDocumentBuilder.Build(new("events", "Events", "module", "1.0", ApiDocumentFormat.AsyncApi),
            [new AsyncApiOperation("send", "topic", AsyncApiAction.Send, message, "kafka")]);
        var payload = document["components"]!["messages"]!["Event"]!["payload"]!["schema"]!;
        payload["$ref"]!.GetValue<string>().ShouldBe("#anchor");
        payload["$dynamicRef"]!.GetValue<string>().ShouldBe("#dynamic-anchor");
        payload["anyOf"]![0]!["$ref"]!.GetValue<bool>().ShouldBeFalse();
        payload["anyOf"]![0]!["$dynamicRef"]!.GetValue<bool>().ShouldBeFalse();
    }

    private sealed class Source(params ApiDocumentDescriptor[] documents) : IApiDocumentSource
    {
        public IReadOnlyCollection<ApiDocumentDescriptor> GetDocuments() => documents;
    }

    public sealed class EmptySource : IApiDocumentSource
    {
        public IReadOnlyCollection<ApiDocumentDescriptor> GetDocuments() => [];
    }
}

internal sealed record Payload(Guid Id, string? Status);

internal sealed record RecursivePayload(RecursivePayload? Next, IReadOnlyList<RecursivePayload>? Children);

[JsonSerializable(typeof(Payload))]
[JsonSerializable(typeof(RecursivePayload))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class ContractJsonContext : JsonSerializerContext;
