namespace PANiXiDA.Core.ApiDocumentation.AsyncApi;

/// <summary>Describes message flow from the documented application's perspective.</summary>
public enum AsyncApiAction
{
    /// <summary>The application sends a message.</summary>
    Send = 1,
    /// <summary>The application receives a message.</summary>
    Receive = 2
}
