using System.Text.Json.Nodes;

namespace PANiXiDA.Core.ApiDocumentation;

/// <summary>Builds AsyncAPI documents without depending on a web host.</summary>
public interface IAsyncApiDocumentSource : IApiDocumentSource
{
    /// <summary>Builds a registered document.</summary>
    /// <param name="documentName">The name returned by GetDocuments.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The complete AsyncAPI JSON document.</returns>
    JsonObject Generate(string documentName, CancellationToken cancellationToken = default);
}
