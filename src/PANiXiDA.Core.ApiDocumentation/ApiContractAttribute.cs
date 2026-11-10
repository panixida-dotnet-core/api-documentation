namespace PANiXiDA.Core.ApiDocumentation;

/// <summary>Assigns a messaging contract to a module and API version.</summary>
[AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class | AttributeTargets.Interface, AllowMultiple = false, Inherited = false)]
public sealed class ApiContractAttribute : Attribute
{
    /// <summary>Creates contract documentation metadata.</summary>
    /// <param name="module">The stable module name.</param>
    /// <param name="version">The API contract version.</param>
    public ApiContractAttribute(string module, string version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(module);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        Module = module;
        Version = version;
    }

    /// <summary>Gets the stable module name.</summary>
    public string Module { get; }
    /// <summary>Gets the contract version.</summary>
    public string Version { get; }
    /// <summary>Gets or sets the optional human-readable title.</summary>
    public string? Title { get; set; }
    /// <summary>Gets or sets whether the contract is deprecated.</summary>
    public bool Deprecated { get; set; }
}
