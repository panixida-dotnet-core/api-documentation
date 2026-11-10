# PANiXiDA.Core.ApiDocumentation

Renderer-independent API document contracts and AOT-compatible AsyncAPI generation for .NET 10.

## Status

[![CI](https://github.com/panixida-dotnet-core/api-documentation/actions/workflows/ci.yml/badge.svg)](https://github.com/panixida-dotnet-core/api-documentation/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/PANiXiDA.Core.ApiDocumentation.svg)](https://www.nuget.org/packages/PANiXiDA.Core.ApiDocumentation)
[![NuGet downloads](https://img.shields.io/nuget/dt/PANiXiDA.Core.ApiDocumentation.svg)](https://www.nuget.org/packages/PANiXiDA.Core.ApiDocumentation)
[![Target Framework](https://img.shields.io/badge/target-net10.0-512BD4)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/github/license/panixida-dotnet-core/api-documentation.svg)](LICENSE)

## Overview

This package supplies the shared documentation model used by PANiXiDA transport libraries. It combines document identities in one catalog and builds AsyncAPI documents from transport-owned metadata.

HTTP owns OpenAPI generation, SignalR owns hub discovery, and Wolverine owns Kafka topology. `PANiXiDA.Core.Presentation.Scalar` presents their documents. This package has no dependency on those adapters, ASP.NET Core, or a message broker, so workers can also generate or export documents directly.

## Features

- Shared OpenAPI and AsyncAPI document descriptors, module names, versions, and routes.
- Deterministic catalog ordering and duplicate name/path validation.
- AsyncAPI 3.0 channels, send/receive operations, messages, replies, servers, bindings, and bearer requirements.
- JSON Schema export from registered source-generated `JsonSerializerContext` metadata.
- Stable operation IDs with independent display names and summaries.
- Shared message components and rebased local schema references.
- Dependency injection registration for document sources and schema contexts.

## Quick Start

### Requirements

- .NET 10 SDK compatible with `global.json`.
- A .NET project targeting `net10.0`.
- Source-generated JSON metadata for payload types whose schemas are exported.

### Installation

```shell
dotnet add package PANiXiDA.Core.ApiDocumentation
```

### Minimal import

```csharp
using PANiXiDA.Core.ApiDocumentation;
using PANiXiDA.Core.ApiDocumentation.AsyncApi;
```

### First example

Generate a document directly, without a web host or broker:

```csharp
using System.Text.Json.Serialization;

using PANiXiDA.Core.ApiDocumentation;
using PANiXiDA.Core.ApiDocumentation.AsyncApi;

var schemas = new ApiSchemaRegistry([ApplicationJsonContext.Default]);
var message = new AsyncApiMessage("StateChanged", schemas.GetSchema(typeof(StateChanged)));
var descriptor = new ApiDocumentDescriptor(
    "matchmaking-signalr-v1", "Matchmaking SignalR v1", "matchmaking", "1.0",
    ApiDocumentFormat.AsyncApi);
var operation = new AsyncApiOperation(
    "state-changed", "/hubs/state", AsyncApiAction.Send, message, "ws",
    summary: "Notifies clients when search state changes.")
{
    Name = "Search state changed"
};

var document = AsyncApiDocumentBuilder.Build(descriptor, [operation]);
Console.WriteLine(document.ToJsonString());

internal sealed record StateChanged(Guid Id, string? Status);

[JsonSerializable(typeof(StateChanged))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class ApplicationJsonContext : JsonSerializerContext;
```

For normal application integration, use a transport adapter to discover its operations and provide a document source. Direct generation is useful for custom adapters and exports.

## Usage

### Document sources and dependency injection

An `IApiDocumentSource` lists descriptors. An `IAsyncApiDocumentSource` also generates their AsyncAPI JSON. OpenAPI sources can contribute descriptors while their transport maps the corresponding document endpoints.

Register a source and the payload contexts in the host:

```csharp
using PANiXiDA.Core.ApiDocumentation.DependencyInjection;

builder.Services.AddApiDocumentSource<StateDocumentSource>();
builder.Services.AddApiDocumentationSchemas(ApplicationJsonContext.Default);
```

A custom source can reuse the payload and context from the first example:

```csharp
using System.Text.Json.Nodes;

using PANiXiDA.Core.ApiDocumentation;
using PANiXiDA.Core.ApiDocumentation.AsyncApi;

public sealed class StateDocumentSource(ApiSchemaRegistry schemas) : IAsyncApiDocumentSource
{
    private static readonly ApiDocumentDescriptor Descriptor = new(
        "matchmaking-events-v1", "Matchmaking events v1", "matchmaking", "1.0",
        ApiDocumentFormat.AsyncApi);

    public IReadOnlyCollection<ApiDocumentDescriptor> GetDocuments()
    {
        return [Descriptor];
    }

    public JsonObject Generate(string documentName, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (documentName != Descriptor.Name)
        {
            throw new ArgumentException("Unknown document.", nameof(documentName));
        }

        var message = new AsyncApiMessage("StateChanged", schemas.GetSchema(typeof(StateChanged)));
        var operation = new AsyncApiOperation(
            "publish-state", "matchmaking.state", AsyncApiAction.Send, message, "kafka")
        {
            Name = "Publish search state"
        };
        return AsyncApiDocumentBuilder.Build(Descriptor, [operation]);
    }
}
```

Sources are registered as singletons. Repeated registration of the same source type is ignored. Register all sources before resolving `ApiDocumentCatalog`.

### Modules, versions, and routes

`ApiDocumentDescriptor` carries `Name`, `Title`, `Module`, `Version`, `Format`, `Path`, and `Deprecated`. The default path is `/openapi/{name}.json` or `/asyncapi/{name}.json`. An explicit path can override it.

`ApiDocumentNames.Create("Matchmaking", "SignalR", "1.0")` produces `matchmaking-signalr-v1`; version `1.2` produces `matchmaking-signalr-v1.2`.

The catalog rejects duplicate names and paths ignoring case, and sorts descriptors by module and name using ordinal comparison. Versions describe contracts; adapters own endpoint routing and message version negotiation.

### Operation names, direction, and replies

`AsyncApiOperation.Id` is the stable document key. `Name` is the displayed title; when omitted, the title falls back to `Id`. `Summary` is an optional description. `ApiOperationMetadata` shares the same display-name/summary contract between adapters.

Directions use the documented application's perspective: `Send` publishes a message, while `Receive` consumes one. `Reply` describes a reply message; it does not introduce HTTP response status codes.

Use `Bindings` for protocol-specific metadata and `ServerName` to associate a channel with a configured server. Pass `AsyncApiServer` entries to `Build`; `RequiresBearer` adds a bearer security scheme and operation requirement. Documented hosts must omit credentials.

## Configuration

This package has no `appsettings.json` section, ports, or external service requirements. Consumers provide sources, document metadata, and payload JSON contexts through code or DI.

### JSON contracts and Native AOT

`ApiSchemaRegistry.GetSchema` uses only registered `JsonSerializerContext` metadata. Missing payload metadata throws `InvalidOperationException`; there is no reflection fallback. Context naming policies, converters, and serialization settings must match the actual transport. Custom converters may require explicit schemas.

The package is marked AOT-compatible. Complete application compatibility also depends on its transport, serializer, and other dependencies.

## Project Structure

```text
.
├── src/PANiXiDA.Core.ApiDocumentation/
│   ├── AsyncApi/
│   ├── DependencyInjection/
│   └── PANiXiDA.Core.ApiDocumentation.csproj
├── tests/PANiXiDA.Core.ApiDocumentation.UnitTests/
├── .github/workflows/ci.yml
├── Directory.Build.props
├── Directory.Build.targets
├── Directory.Packages.props
├── global.json
├── version.json
├── icon.png
├── LICENSE
└── README.md
```

### Main repository files

`src/` contains public contracts and document generation; `tests/` contains behavioral tests. Shared build and package settings live in `Directory.Build.props` and `Directory.Build.targets`. Package versions are centralized in `Directory.Packages.props`; `global.json` selects the SDK/test runner, and `version.json` controls package versioning.

## Development

### Build

```shell
dotnet restore
dotnet build --configuration Release
```

### Format

```shell
dotnet format
```

### Test

```shell
dotnet test --configuration Release
```

### Coverage

```shell
dotnet test --configuration Release -- --coverage --coverage-output coverage.xml --coverage-output-format xml
```

### Pack

```shell
dotnet pack --configuration Release
```

### Full local validation

```shell
dotnet restore
dotnet format
dotnet build --configuration Release
dotnet test --configuration Release
```

### Continuous integration

Every pull request and push to `main` runs formatting, tests, and mandatory SonarQube analysis. Publishing from `main` requires successful formatting, tests, and the SonarQube Quality Gate.

### Tooling and conventions

- .NET 10, nullable reference types, and implicit usings.
- Central package management through `Directory.Packages.props`.
- Microsoft Testing Platform, xUnit v3, and Shouldly.
- Nerdbank.GitVersioning through `version.json`.
- Package metadata, README, and icon inclusion through `Directory.Build.targets`.

## API / Contracts / Examples

### Core types

| API | Responsibility |
| --- | --- |
| `ApiDocumentDescriptor`, `ApiDocumentFormat` | Document identity, format, module, version, and path |
| `ApiDocumentCatalog` | Aggregate sources and validate document identities |
| `IApiDocumentSource` | List transport-owned document descriptors |
| `IAsyncApiDocumentSource` | Generate AsyncAPI documents by name |
| `ApiSchemaRegistry` | Export payload schemas from generated JSON metadata |
| `ApiDocumentNames` | Normalize module/transport/version document names |
| `ApiOperationMetadata` | Display name and optional summary |
| `AsyncApiOperation`, `AsyncApiAction` | Operation identity, direction, payload, and reply |
| `AsyncApiMessage` | Message identity and an independent payload schema |
| `AsyncApiServer` | Named server host and protocol |
| `AsyncApiDocumentBuilder` | Build AsyncAPI 3.0 JSON |

### Behavioral notes

- `OpenApi = 1`, `AsyncApi = 2`; `Send = 1`, `Receive = 2`. Zero is invalid.
- Duplicate operation IDs, conflicting message definitions, incompatible channels, or unknown servers fail explicitly.
- When servers are configured, every channel must match a server by protocol and optional `ServerName`. Omitting servers or passing an empty list remains supported.
- Payload schemas declare JSON Schema draft 2020-12. Local references are rebased without rewriting literal data or resources with an explicit `$id`, including nested resources.
- The builder implements the AsyncAPI subset used by Core adapters. It does not discover transports, host a UI, or generate clients.

### Package contents

The package includes the `net10.0` library, XML documentation, README, package icon, and Source Link metadata. Repository packaging also produces a symbols package.

## Roadmap / TODO

Track proposed changes and known issues in [GitHub Issues](https://github.com/panixida-dotnet-core/api-documentation/issues). New capabilities should follow concrete transport and consumer requirements.

## Contributing

### General rules

Keep the public API focused, preserve existing contracts, and justify new dependencies. Public APIs require English XML documentation. Update this README when usage, configuration, or package behavior changes.

### Code style

Follow `.editorconfig` and existing naming. Prefer readable implementations and separate documented members with blank lines.

### Tests

Cover meaningful behavior changes, guard clauses, and failures. Use Arrange, Act, Assert and English `DisplayName` values. Test projects provide global `Xunit` and `Shouldly` imports.

### Validation before completion

Run the commands in [Full local validation](#full-local-validation). Keep generated build output and temporary diagnostics out of commits.

## License

Licensed under the Apache License, Version 2.0. See [LICENSE](LICENSE).

## Maintainers / Contacts

Maintained by [PANiXiDA](https://github.com/PANiXiDA).

Use [Issues](https://github.com/panixida-dotnet-core/api-documentation/issues) for questions and defects, and [Pull Requests](https://github.com/panixida-dotnet-core/api-documentation/pulls) for proposed changes.
