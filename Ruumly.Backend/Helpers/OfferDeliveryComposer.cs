using System.Globalization;
using System.Text;
using Ruumly.Backend.Constants;
using Ruumly.Backend.DTOs.Responses;
using Ruumly.Backend.Models;

namespace Ruumly.Backend.Helpers;

public static class OfferDeliveryComposer
{
    public static PublicOfferDto ToPublic(Offer offer)
    {
        var lead = offer.DemandLead;
        return new PublicOfferDto(
            offer.Status.ToString().ToLowerInvariant(),
            offer.Language,
            offer.CustomerNote,
            offer.SentAt,
            offer.ChosenOptionId,
            lead is null
                ? null
                : new PublicOfferLeadDto(
                    ServiceCategories.SlugFor(lead.Category),
                    lead.City,
                    lead.ToCity,
                    lead.NeedDate,
                    lead.Details),
            offer.Options
                .OrderBy(option => option.SortOrder)
                .ThenBy(option => option.Id)
                .Select(option => new PublicOfferOptionDto(
                    option.Id,
                    option.Title,
                    option.PriceAmount,
                    option.PriceUnit,
                    option.Notes,
                    option.Supplier?.Name))
                .ToList());
    }

    public static OfferEmailMessage ComposeEmail(Offer offer, string link)
    {
        var translations = EmailTranslations.For(offer.Language);
        var body = new StringBuilder();
        body.AppendLine(translations.OfferGreeting);
        body.AppendLine();
        body.AppendLine(translations.OfferIntro);
        body.AppendLine();

        var index = 1;
        foreach (var option in offer.Options.OrderBy(o => o.SortOrder).ThenBy(o => o.Id))
        {
            body.AppendLine($"{index}. {option.Title}{FormatPrice(option)}");
            if (!string.IsNullOrWhiteSpace(option.Notes))
                body.AppendLine($"   {option.Notes}");
            index++;
        }

        if (!string.IsNullOrWhiteSpace(offer.CustomerNote))
        {
            body.AppendLine();
            body.AppendLine($"{translations.OfferNoteLabel} {offer.CustomerNote}");
        }

        body.AppendLine();
        body.AppendLine(translations.OfferCta);
        body.AppendLine(link);
        body.AppendLine();
        body.AppendLine(translations.OfferQuestions);
        body.AppendLine();
        body.Append(translations.OfferSignature);

        return new OfferEmailMessage(translations.OfferSubject, body.ToString(), link);
    }

    private static string FormatPrice(OfferOption option)
    {
        if (option.PriceAmount is not { } amount) return "";

        var unit = DisplayUnit(option.PriceUnit);
        return $" \u2014 {amount.ToString("0.##", CultureInfo.InvariantCulture)} \u20ac"
             + (unit.Length == 0 ? "" : $" / {unit}");
    }

    /// <summary>
    /// The unit without the separator it already carries. A stored unit comes in
    /// whatever shape the surface that wrote it uses: the provider quote form and
    /// PriceUnitNormalizer both emit the leading-slash shape ("/kuu"), and an
    /// option prefilled from a listing can arrive with the currency glued on in
    /// front of that. Printing it straight after our own separator is what put a
    /// doubled slash into the price the customer read.
    ///
    /// Deliberately the same normalisation the offer page performs
    /// (estonia-space-hub OfferPresentation.displayPriceUnit): the email and the
    /// page it links to show one customer the same price, so a change to either
    /// belongs in both. A unit carrying no separator of its own (a bare "kuu", or
    /// a one-time fee) is passed through untouched - the caller adds the " / ".
    /// </summary>
    private static string DisplayUnit(string? priceUnit)
    {
        var unit = priceUnit?.Trim() ?? "";
        if (unit.StartsWith('\u20ac')) unit = unit[1..].TrimStart();
        if (unit.StartsWith('/')) unit = unit[1..].TrimStart();
        return unit.TrimEnd();
    }
}
