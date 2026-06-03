namespace Ruumly.Backend.Identity.MobileId;

/// <summary>
/// Configuration for the SK Mobile-ID RP-API.
///
/// <para>
/// Map to <c>MobileId:*</c> in appsettings / environment variables.
/// The Relying Party UUID and Name are shared with Smart-ID (the same SK account
/// grants access to both); only the base URL differs.
/// The integration is disabled (env-gated) when <c>SmartId:RelyingPartyUuid</c> is empty.
/// </para>
/// </summary>
public sealed class MobileIdConfig
{
    /// <summary>
    /// Base URL for the Mobile-ID RP-API.
    /// Demo: <c>https://tsp.demo.sk.ee/mid-api</c>.
    /// Production: <c>https://mid.sk.ee/mid-api</c>.
    /// </summary>
    public string BaseUrl { get; set; } = "https://tsp.demo.sk.ee/mid-api";

    /// <summary>
    /// Optional HMAC secret override. When empty, falls back to <c>SmartId:HmacSecret</c>.
    /// </summary>
    public string HmacSecret { get; set; } = "";
}
