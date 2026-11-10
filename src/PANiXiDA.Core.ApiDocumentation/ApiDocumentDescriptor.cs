namespace PANiXiDA.Core.ApiDocumentation;

/// <summary>Identifies a versioned document independently of its renderer.</summary>
public sealed class ApiDocumentDescriptor
{
    /// <summary>Creates a document descriptor.</summary>
    /// <param name="name">The unique document name used in its URL.</param>
    /// <param name="title">The human-readable API title.</param>
    /// <param name="module">The owning module name.</param>
    /// <param name="version">The described contract version.</param>
    /// <param name="format">The specification format.</param>
    /// <param name="path">An optional absolute application path.</param>
    /// <param name="deprecated">Whether the contract is deprecated.</param>
    public ApiDocumentDescriptor(string name, string title, string module, string version,
        ApiDocumentFormat format, string? path = null, bool deprecated = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(module);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        if (!name.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.'))
        {
            throw new ArgumentException("Document names must contain only ASCII letters, digits, dots, underscores or hyphens.", nameof(name));
        }

        if (!Enum.IsDefined(format))
        {
            throw new ArgumentOutOfRangeException(nameof(format));
        }

        var documentPath = path ?? $"/{(format == ApiDocumentFormat.OpenApi ? "openapi" : "asyncapi")}/{name}.json";
        if (!documentPath.StartsWith('/') || documentPath.StartsWith("//", StringComparison.Ordinal)
            || documentPath.Contains("..", StringComparison.Ordinal)
            || documentPath.Any(character => char.IsWhiteSpace(character) || char.IsControl(character))
            || documentPath.IndexOfAny(['?', '#', '{', '}', '\\']) >= 0)
        {
            throw new ArgumentException("Document paths must be literal absolute application paths.", nameof(path));
        }

        Name = name;
        Title = title;
        Module = module;
        Version = version;
        Format = format;
        Path = documentPath;
        Deprecated = deprecated;
    }

    /// <summary>Gets the unique document name.</summary>
    public string Name { get; }

    /// <summary>Gets the API title.</summary>
    public string Title { get; }

    /// <summary>Gets the owning module.</summary>
    public string Module { get; }

    /// <summary>Gets the contract version.</summary>
    public string Version { get; }

    /// <summary>Gets the specification format.</summary>
    public ApiDocumentFormat Format { get; }

    /// <summary>Gets the document's application path.</summary>
    public string Path { get; }

    /// <summary>Gets whether this contract is deprecated.</summary>
    public bool Deprecated { get; }
}
