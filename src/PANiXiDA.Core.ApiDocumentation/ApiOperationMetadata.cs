namespace PANiXiDA.Core.ApiDocumentation;

/// <summary>Provides a display name and optional description independently of an operation's transport identity.</summary>
/// <param name="Name">The nonempty display name.</param>
/// <param name="Summary">The optional description. An empty string explicitly clears a lower-priority description.</param>
public sealed record ApiOperationMetadata(string Name, string? Summary = null)
{
    /// <summary>Gets the display name.</summary>
    public string Name { get; } = ValidateName(Name);

    private static string ValidateName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return name;
    }
}
