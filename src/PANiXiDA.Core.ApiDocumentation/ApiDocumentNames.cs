namespace PANiXiDA.Core.ApiDocumentation;

/// <summary>Creates stable document names from module, transport and contract version.</summary>
public static class ApiDocumentNames
{
    /// <summary>Creates a lowercase document identifier.</summary>
    /// <param name="module">The stable module name.</param>
    /// <param name="transport">The documented transport.</param>
    /// <param name="version">The contract version.</param>
    /// <returns>A name such as matchmaking-signalr-v1.</returns>
    public static string Create(string module, string transport, string version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(module);
        ArgumentException.ThrowIfNullOrWhiteSpace(transport);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        var normalizedVersion = version;
        while (normalizedVersion.EndsWith(".0", StringComparison.Ordinal))
        {
            normalizedVersion = normalizedVersion[..^2];
        }

        var name = $"{module}-{transport}-v{normalizedVersion}".ToLowerInvariant();
        if (!name.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.'))
        {
            throw new ArgumentException("Module, transport and version must form a valid document identifier.");
        }

        return name;
    }
}
