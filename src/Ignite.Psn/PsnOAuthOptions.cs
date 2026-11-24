namespace Ignite.Psn;

/// <summary>
/// OAuth client of the official Remote Play app (docs/protocol/02-psn-account.md). The id and
/// secret are not part of the repository and must be supplied by configuration.
/// </summary>
public sealed record PsnOAuthOptions(string ClientId, string ClientSecret)
{
    public const string AuthorizeEndpoint = "https://auth.api.sonyentertainmentnetwork.com/2.0/oauth/authorize";
    public const string TokenEndpoint = "https://auth.api.sonyentertainmentnetwork.com/2.0/oauth/token";
    public const string RedirectUri = "https://remoteplay.dl.playstation.net/remoteplay/redirect";
    public const string Scope =
        "psn:clientapp referenceDataService:countryConfig.read pushNotification:webSocket.desktop.connect sessionManager:remotePlaySession.system.update";

    public const string ClientIdVariable = "IGNITE_PSN_CLIENT_ID";
    public const string ClientSecretVariable = "IGNITE_PSN_CLIENT_SECRET";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);

    /// <summary>Environment variables first, then the given (settings) values.</summary>
    public static PsnOAuthOptions Resolve(string? clientId = null, string? clientSecret = null)
    {
        var fromEnvironment = new PsnOAuthOptions(
            Environment.GetEnvironmentVariable(ClientIdVariable) ?? "",
            Environment.GetEnvironmentVariable(ClientSecretVariable) ?? "");
        return fromEnvironment.IsConfigured ? fromEnvironment : new PsnOAuthOptions(clientId ?? "", clientSecret ?? "");
    }
}
