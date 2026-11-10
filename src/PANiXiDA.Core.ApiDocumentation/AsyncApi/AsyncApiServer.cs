namespace PANiXiDA.Core.ApiDocumentation.AsyncApi;

/// <summary>Describes a configured messaging server.</summary>
public sealed class AsyncApiServer
{
    /// <summary>Creates a server description.</summary>
    /// <param name="name">The stable server identifier.</param>
    /// <param name="host">The host and optional port, without credentials.</param>
    /// <param name="protocol">The AsyncAPI protocol name.</param>
    public AsyncApiServer(string name, string host, string protocol)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        ArgumentException.ThrowIfNullOrWhiteSpace(protocol);
        if (host.Contains('@') || host.Any(char.IsControl))
        {
            throw new ArgumentException("Documented server hosts must not contain credentials or control characters.", nameof(host));
        }

        Name = name;
        Host = host;
        Protocol = protocol;
    }

    /// <summary>Gets the server identifier.</summary>
    public string Name { get; }

    /// <summary>Gets the server host.</summary>
    public string Host { get; }

    /// <summary>Gets the protocol name.</summary>
    public string Protocol { get; }
}
