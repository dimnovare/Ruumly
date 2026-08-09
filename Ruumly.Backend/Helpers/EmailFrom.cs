namespace Ruumly.Backend.Helpers;

/// <summary>
/// The From header every outbound email carries, built from configuration
/// (<c>Email:FromName</c> / <c>Email:FromAddress</c>, i.e. the Railway env vars
/// <c>Email__FromName</c> / <c>Email__FromAddress</c>).
///
/// Extracted so the supplier-introduction dry run can show the founder the
/// EXACT From line before approving a cold campaign. If this ever drifts from
/// what ResendEmailSender actually sends, the preview becomes a lie — so both
/// read this one method.
/// </summary>
public static class EmailFrom
{
    public const string DefaultName    = "Ruumly";
    public const string DefaultAddress = "noreply@ruumly.eu";

    public static string Render(IConfiguration config) =>
        $"{config["Email:FromName"] ?? DefaultName} <{config["Email:FromAddress"] ?? DefaultAddress}>";
}
