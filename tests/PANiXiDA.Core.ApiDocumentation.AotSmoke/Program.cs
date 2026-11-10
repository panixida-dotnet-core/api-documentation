using PANiXiDA.Core.ApiDocumentation.AotSmoke;
using PANiXiDA.Core.ApiDocumentation;
using PANiXiDA.Core.ApiDocumentation.AsyncApi;

var schemas = new ApiSchemaRegistry([SmokeJsonContext.Default]);
var message = new AsyncApiMessage("StateChanged", schemas.GetSchema(typeof(StateChanged)));
var descriptor = new ApiDocumentDescriptor("matchmaking-signalr-v1", "Matchmaking", "matchmaking", "1.0", ApiDocumentFormat.AsyncApi);
var document = AsyncApiDocumentBuilder.Build(descriptor,
    [new AsyncApiOperation("StateChanged", "/hubs/state", AsyncApiAction.Send, message, "signalr")]);
if (document["components"]!["messages"]!["StateChanged"]!["payload"]!["schema"]!["properties"]!["id"] is null)
{
    throw new InvalidOperationException("The AOT document is missing its payload schema.");
}

Console.WriteLine(document.ToJsonString());
