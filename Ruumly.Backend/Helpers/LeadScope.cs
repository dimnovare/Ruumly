using System.Text.Json;
using Ruumly.Backend.Constants;

namespace Ruumly.Backend.Helpers;

/// <summary>
/// Reading <see cref="Models.DemandLead.ScopeJson"/> back.
///
/// One place, because the column is JSON in a text field and every caller that
/// parsed it itself would be a caller that could throw on a malformed value.
/// The scoping answers are an optional extra on a request — a bad parse must
/// never be able to take down the outreach email or the quote page that carry
/// the request itself. Exactly the contract <see cref="LeadPhotos"/> has, for
/// exactly the same reason.
/// </summary>
public static class LeadScope
{
    /// <summary>
    /// The lead's answers, validated and in catalogue order, or empty. Never
    /// throws.
    ///
    /// READS BOTH STORED SHAPES, and neither is a legacy to be migrated away
    /// from. <c>{"movingSize":3}</c> is a single-choice answer and is what every
    /// row written before tick-all-that-apply arrived carries;
    /// <c>{"movingHeavyItems":[2,4]}</c> is a question that takes several. A
    /// question that accepts a list still stores a bare number when one box is
    /// ticked, so the shape of an existing row — and what it renders — is
    /// untouched by the second shape existing.
    ///
    /// Re-validated on the way OUT as well as in. The column is written by us
    /// today, but a stored row outlives the build that wrote it: a question we
    /// have since retired, or a chip count we have since shortened, would
    /// otherwise render a fact line this build has no wording for. Dropping it
    /// here means the email loses one line instead of gaining a bare number
    /// nobody can quote against.
    /// </summary>
    public static IReadOnlyList<ScopeAnswer> Answers(string? scopeJson)
    {
        if (string.IsNullOrWhiteSpace(scopeJson)) return [];

        try
        {
            // JsonDocument rather than a Dictionary&lt;string,JsonElement&gt;
            // deserialize: one junk value ("movingSize": null) must cost that
            // one answer, not the whole object. Normalize inspects each value
            // itself and keeps the chip positions that are in range.
            using var doc = JsonDocument.Parse(scopeJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return [];

            // Materialized INSIDE the using: JsonElement is a window into the
            // document's buffer and is invalid once it is disposed. Normalize
            // returns lists of ints, so nothing borrowed escapes this scope.
            return ScopeQuestions.Normalize(
                doc.RootElement.EnumerateObject()
                   .Select(p => KeyValuePair.Create(p.Name, p.Value)));
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>How many scoping questions this lead actually answered.</summary>
    public static int Count(string? scopeJson) => Answers(scopeJson).Count;
}
