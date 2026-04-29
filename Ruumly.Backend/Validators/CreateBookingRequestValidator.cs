using FluentValidation;
using Ruumly.Backend.DTOs.Requests;

namespace Ruumly.Backend.Validators;

public class CreateBookingRequestValidator : AbstractValidator<CreateBookingRequest>
{
    public CreateBookingRequestValidator()
    {
        RuleFor(x => x.ListingId)
            .NotEmpty().WithMessage("ListingId is required");

        RuleFor(x => x.StartDate)
            .NotEmpty().WithMessage("StartDate is required")
            .Matches(@"^\d{4}-\d{2}-\d{2}$")
            .WithMessage("StartDate must be yyyy-MM-dd")
            .Must(BeNotInPast).WithMessage("Start date cannot be in the past.");

        RuleFor(x => x.Duration)
            .NotEmpty().WithMessage("Duration is required")
            .MaximumLength(50);

        RuleFor(x => x.ContactName)
            .NotEmpty().WithMessage("Contact name is required")
            .MaximumLength(100);

        RuleFor(x => x.ContactEmail)
            .NotEmpty().EmailAddress()
            .WithMessage("Valid contact email is required");

        RuleFor(x => x.ContactPhone)
            .NotEmpty().WithMessage("Contact phone is required")
            .MaximumLength(20);

        RuleFor(x => x.EndDate)
            .Must((req, endDate) =>
            {
                if (string.IsNullOrEmpty(endDate)) return true;
                if (!DateTime.TryParse(req.StartDate, out var start)) return true;
                if (!DateTime.TryParse(endDate, out var end)) return true;
                return end > start;
            })
            .WithMessage("Lõppkuupäev peab olema alguskuupäevast hiljem.")
            .When(x => !string.IsNullOrEmpty(x.EndDate));

        RuleFor(x => x.Notes)
            .MaximumLength(1000).When(x => x.Notes != null);

        RuleFor(x => x.Extras)
            .Must(e => e.Count <= 10)
            .WithMessage("Maximum 10 extras allowed");

        RuleForEach(x => x.Extras)
            .NotEmpty()
            .MaximumLength(50)
            .Matches(@"^[a-z0-9-]+$")
            .WithMessage("Extra key must be lowercase alphanumeric with hyphens only: {PropertyValue}");
    }

    // Allow today (UTC-tolerant: 1-day buffer for client/server timezone drift).
    private static bool BeNotInPast(string startDate)
    {
        if (!DateTime.TryParseExact(
                startDate, "yyyy-MM-dd",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None,
                out var parsed))
            return true; // format check elsewhere; don't double-fail
        return parsed.Date >= DateTime.UtcNow.Date.AddDays(-1);
    }
}
