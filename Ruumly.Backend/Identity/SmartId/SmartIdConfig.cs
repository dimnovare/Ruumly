namespace Ruumly.Backend.Identity.SmartId;

/// <summary>
/// Configuration for the SK Smart-ID RP-API v2/v3.
///
/// <para>
/// Map to <c>SmartId:*</c> in appsettings / environment variables.
/// The integration is disabled (env-gated) when <see cref="RelyingPartyUuid"/> is empty.
/// </para>
/// </summary>
public sealed class SmartIdConfig
{
    /// <summary>
    /// The Relying Party UUID assigned by SK in the Smart-ID portal.
    /// An empty value disables the integration.
    /// </summary>
    public string RelyingPartyUuid { get; set; } = "";

    /// <summary>
    /// The display name shown in the Smart-ID app during authentication, e.g. "Ruumly".
    /// </summary>
    public string RelyingPartyName { get; set; } = "Ruumly";

    /// <summary>
    /// Base URL for the Smart-ID RP-API.
    /// Demo: <c>https://sid.demo.sk.ee/smart-id-rp/v2</c>.
    /// Production: <c>https://rp-api.smart-id.com/v2</c>.
    /// </summary>
    public string BaseUrl { get; set; } = "https://sid.demo.sk.ee/smart-id-rp/v2";

    /// <summary>Server secret used to HMAC-SHA256 personal codes before storing. Set via <c>SmartId:HmacSecret</c>.</summary>
    public string HmacSecret { get; set; } = "";

    /// <summary>True when real credentials are configured.</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(RelyingPartyUuid);
}
