# PANiXiDA.Core.ApiDocumentation

Renderer-independent API document contracts and AsyncAPI generation for .NET 10.

[![CI](https://github.com/panixida-dotnet-core/api-documentation/actions/workflows/ci.yml/badge.svg)](https://github.com/panixida-dotnet-core/api-documentation/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/PANiXiDA.Core.ApiDocumentation.svg)](https://www.nuget.org/packages/PANiXiDA.Core.ApiDocumentation)
[![License](https://img.shields.io/github/license/panixida-dotnet-core/api-documentation.svg)](LICENSE)

## Overview

Transport adapters provide their own document sources. This package combines document identities and generates AsyncAPI JSON without depending on ASP.NET Core, Scalar or a broker.

## Installation

```shell
dotnet add package PANiXiDA.Core.ApiDocumentation
```

## Usage

```csharp
using System.Text.Json.Serialization;
using PANiXiDA.Core.ApiDocumentation;
using PANiXiDA.Core.ApiDocumentation.AsyncApi;

var schemas = new ApiSchemaRegistry([ApplicationJsonContext.Default]);
var message = new AsyncApiMessage("StateChanged", schemas.GetSchema(typeof(StateChanged)));
var descriptor = new ApiDocumentDescriptor(
    "matchmaking-signalr-v1", "Matchmaking", "matchmaking", "1.0",
    ApiDocumentFormat.AsyncApi);
var document = AsyncApiDocumentBuilder.Build(descriptor,
[
    new AsyncApiOperation("StateChanged", "/hubs/state",
        AsyncApiAction.Send, message, "signalr")
]);
Console.WriteLine(document.ToJsonString());

internal sealed record StateChanged(Guid Id, string? Status);

[JsonSerializable(typeof(StateChanged))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class ApplicationJsonContext : JsonSerializerContext;
```

For DI, register sources with `AddApiDocumentSource<TSource>()` and generated contexts with `AddApiDocumentationSchemas(context)`. Presenters enumerate `ApiDocumentCatalog`; workers can export the same documents directly.

## Contracts and versions

`ApiDocumentDescriptor` identifies a module, version, format and document path. Names and paths must be unique. `ApiContractAttribute` provides module/version metadata to transport generators.

Operations use the application's perspective: `Send` means publishing, `Receive` means consuming. Replies are explicit message contracts.

Enum identifiers start at 1: `OpenApi = 1`, `AsyncApi = 2`; `Send = 1`, `Receive = 2`. Zero is invalid.

## Native AOT

Schemas use registered `JsonSerializerContext` metadata. Missing types fail without a reflection fallback. Context settings must match transport JSON serialization; custom converters may need explicit schemas.

The builder supports the AsyncAPI 3.0 subset used by Core adapters and declares exported payload schemas as JSON Schema draft 2020-12. It does not generate transport clients or configure a UI.

## Development

```shell
dotnet restore
dotnet format
dotnet build --configuration Release
dotnet test --configuration Release
dotnet publish tests/PANiXiDA.Core.ApiDocumentation.AotSmoke -c Release -r linux-x64
```

Run the published executable to verify document generation with reflection serialization disabled. CI requires tests, formatting and SonarQube before publishing.
