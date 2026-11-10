namespace PANiXiDA.Core.ApiDocumentation;

/// <summary>Combines transport-owned document descriptors and rejects ambiguous names or routes.</summary>
public sealed class ApiDocumentCatalog
{
    private readonly IApiDocumentSource[] _sources;

    /// <summary>Creates a catalog over the registered document sources.</summary>
    /// <param name="sources">The transport document sources.</param>
    public ApiDocumentCatalog(IEnumerable<IApiDocumentSource> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        _sources = [.. sources];
        if (_sources.Any(source => source is null))
        {
            throw new ArgumentException("Document sources must not contain null.", nameof(sources));
        }
    }

    /// <summary>Returns a stable, validated snapshot of all available documents.</summary>
    /// <returns>The descriptors ordered by module and name.</returns>
    public IReadOnlyList<ApiDocumentDescriptor> GetDocuments()
    {
        var documents = _sources.SelectMany(source => source.GetDocuments()).ToArray();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var document in documents)
        {
            if (!names.Add(document.Name))
            {
                throw new InvalidOperationException($"API document name '{document.Name}' is registered more than once.");
            }

            if (!paths.Add(document.Path))
            {
                throw new InvalidOperationException($"API document path '{document.Path}' is registered more than once.");
            }
        }

        return [.. documents.OrderBy(document => document.Module, StringComparer.Ordinal)
            .ThenBy(document => document.Name, StringComparer.Ordinal)];
    }
}
