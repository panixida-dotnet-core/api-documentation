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
            "Read state", bindings, message, true);
        var document = AsyncApiDocumentBuilder.Build(descriptor, [operation],
            [new AsyncApiServer("signalr", "localhost:5000", "signalr"), new AsyncApiServer("kafka", "localhost:9092", "kafka")]);

        document["asyncapi"]!.GetValue<string>().ShouldBe("3.0.0");
        document["info"]!["version"]!.GetValue<string>().ShouldBe("2.0");
        document["info"]!["x-module"]!.GetValue<string>().ShouldBe("module");
        document["info"]!["x-deprecated"]!.GetValue<bool>().ShouldBeTrue();
        document["operations"]!["GetState"]!["action"]!.GetValue<string>().ShouldBe("receive");
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
        var reply = new AsyncApiOperation("z", "topic", AsyncApiAction.Receive, message, "kafka", reply: message);
        var collidingChannel = new AsyncApiOperation("a", "z.reply", AsyncApiAction.Send, message, "kafka");
        Should.Throw<InvalidOperationException>(() => AsyncApiDocumentBuilder.Build(descriptor, [collidingChannel, reply]));
        var server = new AsyncApiServer("kafka", "localhost:9092", "kafka");
        Should.Throw<InvalidOperationException>(() => AsyncApiDocumentBuilder.Build(descriptor, [], [server, server]));
        Should.Throw<ArgumentNullException>(() => AsyncApiDocumentBuilder.Build(null!, []));
        Should.Throw<ArgumentNullException>(() => AsyncApiDocumentBuilder.Build(descriptor, null!));
        Should.Throw<ArgumentException>(() => AsyncApiDocumentBuilder.Build(
            new ApiDocumentDescriptor("http", "HTTP", "module", "1.0", ApiDocumentFormat.OpenApi), []));
    }

    [Fact(DisplayName = "Nested and recursive schema references remain rooted in their document component")]
    public void RewritesSchemaReferences()
    {
        var schema = new JsonObject
        {
            ["$ref"] = "#",
            ["anyOf"] = new JsonArray(new JsonObject { ["$ref"] = "#/$defs/item" }, null,
                new JsonObject { ["$ref"] = "https://example.com/schema" }),
            ["$defs"] = new JsonObject { ["item"] = new JsonObject { ["type"] = "string" } }
        };
        var message = new AsyncApiMessage("item/~", schema);
        var operation = new AsyncApiOperation("send", "topic", AsyncApiAction.Send, message, "kafka");
        var document = AsyncApiDocumentBuilder.Build(
            new ApiDocumentDescriptor("events", "Events", "module", "1.0", ApiDocumentFormat.AsyncApi), [operation]);
        var payload = document["components"]!["messages"]!["item/~"]!["payload"]!["schema"]!;
        payload["$ref"]!.GetValue<string>().ShouldBe("#/components/messages/item~1~0/payload/schema");
        payload["anyOf"]![0]!["$ref"]!.GetValue<string>().ShouldEndWith("/$defs/item");
        payload["anyOf"]![2]!["$ref"]!.GetValue<string>().ShouldBe("https://example.com/schema");
    }

    [Fact(DisplayName = "Public metadata rejects missing required values")]
    public void MetadataGuards()
    {
        var message = new AsyncApiMessage("event", new JsonObject());
        Should.Throw<ArgumentException>(() => new AsyncApiMessage("", new JsonObject()));
        Should.Throw<ArgumentNullException>(() => new AsyncApiMessage("name", null!));
        Should.Throw<ArgumentException>(() => new AsyncApiOperation("", "topic", AsyncApiAction.Send, message, "kafka"));
        Should.Throw<ArgumentException>(() => new AsyncApiOperation("id", "", AsyncApiAction.Send, message, "kafka"));
        Should.Throw<ArgumentNullException>(() => new AsyncApiOperation("id", "topic", AsyncApiAction.Send, null!, "kafka"));
        Should.Throw<ArgumentException>(() => new AsyncApiOperation("id", "topic", AsyncApiAction.Send, message, ""));
        Should.Throw<ArgumentOutOfRangeException>(() => new AsyncApiOperation("id", "topic", (AsyncApiAction)99, message, "kafka"));
        Should.Throw<ArgumentException>(() => new AsyncApiServer("", "host", "kafka"));
        Should.Throw<ArgumentException>(() => new AsyncApiServer("name", "", "kafka"));
        Should.Throw<ArgumentException>(() => new AsyncApiServer("name", "host", ""));
        Should.Throw<ArgumentException>(() => new AsyncApiServer("name", "user@host", "kafka"));
        Should.Throw<ArgumentException>(() => new AsyncApiServer("name", "bad\nhost", "kafka"));
        Should.Throw<ArgumentException>(() => new ApiContractAttribute("", "1.0"));
        Should.Throw<ArgumentException>(() => new ApiContractAttribute("module", ""));
        var contract = new ApiContractAttribute("module", "1.0") { Title = "Title", Deprecated = true };
        contract.Module.ShouldBe("module");
        contract.Version.ShouldBe("1.0");
        contract.Title.ShouldBe("Title");
        contract.Deprecated.ShouldBeTrue();
    }

    [Fact(DisplayName = "Named brokers isolate identical topic addresses and reject unknown servers")]
    public void IsolatesNamedBrokers()
    {
        var descriptor = new ApiDocumentDescriptor("events", "Events", "module", "1.0", ApiDocumentFormat.AsyncApi);
        var message = new AsyncApiMessage("event", new JsonObject { ["type"] = "string" });
        var first = new AsyncApiOperation("first", "events", AsyncApiAction.Send, message, "kafka", serverName: "primary");
        var second = new AsyncApiOperation("second", "events", AsyncApiAction.Receive, message, "kafka", serverName: "external");
        var document = AsyncApiDocumentBuilder.Build(descriptor, [first, second],
            [new AsyncApiServer("primary", "primary:9092", "kafka"), new AsyncApiServer("external", "external:9092", "kafka")]);
        document["channels"]!.AsObject().Count.ShouldBe(2);
        document["channels"]!["primary:events"]!["address"]!.GetValue<string>().ShouldBe("events");
        document["channels"]!["external:events"]!["servers"]![0]!["$ref"]!.GetValue<string>().ShouldBe("#/servers/external");
        Should.Throw<InvalidOperationException>(() => AsyncApiDocumentBuilder.Build(descriptor, [first]));
        first.ServerName.ShouldBe("primary");
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
