namespace PANiXiDA.Core.ApiDocumentation;

/// <summary>Provides the documents owned by a transport adapter.</summary>
public interface IApiDocumentSource
{
    /// <summary>Returns the documents available after application endpoints are mapped.</summary>
    /// <returns>The available document descriptors.</returns>
    IReadOnlyCollection<ApiDocumentDescriptor> GetDocuments();
}
