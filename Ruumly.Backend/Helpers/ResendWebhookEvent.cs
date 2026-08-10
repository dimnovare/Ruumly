using System.Text.Json;

namespace Ruumly.Backend.Helpers;

/// <summary>
/// The parts of a Resend webhook body we act on, normalized.
/// </summary>
/// <param name="Type">Resend event name, e.g. "email.bounced".</param>
/// <param name="Recipients">Lower-cased, de-duplicated "to" addresses.</param>
/// <param name="BounceType">"hard" | "soft" | "complaint" — see <see cref="ResendWebhookEvent.NormalizeBounceType"/>.</param>
/// <param name="BounceSubType">Resend's bounce.subType, verbatim.</param>
/// <param name="Reason">Human-readable failure text, verbatim (truncated).</param>
/// <param name="OccurredAt">Resend's created_at, when parseable.</param>
public sealed record ResendWebhookEvent(
    string Type,
    IReadOnlyList<string> Recipients,
    string? BounceType,
    string? BounceSubType,
    string? Reason,
    DateTime? OccurredAt)
{
    public const string BouncedType    = "email.bounced";
    public const string ComplainedType = "email.complained";

    /// <summary>
    /// The two events that mean "this address did not reach a human". Everything
    /// else Resend may send (delivered, opened, clicked…) is accepted and ignored,
    /// so enabling extra events in the dashboard can never break the endpoint.
    /// </summary>
    public bool IsDeliveryFailure =>
        Type is BouncedType or ComplainedType;

    /// <summary>A hard bounce or a spam complaint retires the address.</summary>
    public bool DisablesAddress =>
        Type == ComplainedType || BounceType == "hard";

    public static bool TryParse(string json, out ResendWebhookEvent? evt)
    {
        evt = null;
        if (string.IsNullOrWhiteSpace(json)) return false;

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return false;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return false;

            var type = ReadString(root, "type");
            if (string.IsNullOrWhiteSpace(type)) return false;

            var occurredAt = ReadDate(root, "created_at");

            var recipients = new List<string>();
            string? bounceType = null, bounceSubType = null, reason = null;

            if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object)
            {
                if (data.TryGetProperty("to", out var to))
                {
                    // Resend sends an array; tolerate a bare string too.
                    if (to.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in to.EnumerateArray())
                            AddRecipient(recipients, item.GetString());
                    }
                    else if (to.ValueKind == JsonValueKind.String)
                    {
                        AddRecipient(recipients, to.GetString());
                    }
                }

                if (data.TryGetProperty("bounce", out var bounce) && bounce.ValueKind == JsonValueKind.Object)
                {
                    bounceType    = NormalizeBounceType(ReadString(bounce, "type"));
                    bounceSubType = ReadString(bounce, "subType");
                    reason        = ReadString(bounce, "message");
                }

                occurredAt ??= ReadDate(data, "created_at");
            }

            if (type == ComplainedType) bounceType = "complaint";

            evt = new ResendWebhookEvent(
                type!, recipients, bounceType, bounceSubType, Truncate(reason, 500), occurredAt);
            return true;
        }
    }

    /// <summary>
    /// Resend/SES bounce classes → our two outcomes. "Undetermined" is deliberately
    /// treated as soft: an ambiguous signal must never retire a live business's
    /// only reachable address.
    /// </summary>
    public static string? NormalizeBounceType(string? raw) => raw?.Trim().ToLowerInvariant() switch
    {
        "permanent"    => "hard",
        "transient"    => "soft",
        "undetermined" => "soft",
        null or ""     => null,
        _              => "soft",
    };

    private static void AddRecipient(List<string> into, string? value)
    {
        var address = value?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(address) || address.Length > 200) return;
        if (!into.Contains(address)) into.Add(address);
    }

    private static string? ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static DateTime? ReadDate(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
        && DateTime.TryParse(
            value.GetString(), null,
            System.Globalization.DateTimeStyles.AdjustToUniversal
            | System.Globalization.DateTimeStyles.AssumeUniversal,
            out var parsed)
            ? parsed
            : null;

    private static string? Truncate(string? value, int max) =>
        value is null || value.Length <= max ? value : value[..max];
}
