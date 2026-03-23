using FluentValidation;
using Ruumly.Backend.DTOs.Requests;

namespace Ruumly.Backend.Validators;

public class CreateBookingRequestValidator : AbstractValidator<CreateBookingRequest>
{
    private static readonly string[] ValidExtras =
        ["packing", "loading", "insurance", "forklift"];

    public CreateBookingRequestValidator()
    {
        RuleFor(x => x.ListingId)
            .NotEmpty().WithMessage("ListingId is required");

        RuleFor(x => x.StartDate)
            .NotEmpty().WithMessage("StartDate is required")
            .Matches(@"^\d{4}-\d{2}-\d{2}$")
            .WithMessage("StartDate must be yyyy-MM-dd");

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

        RuleFor(x => x.Notes)
            .MaximumLength(1000).When(x => x.Notes != null);

        RuleFor(x => x.Extras)
            .Must(e => e.Count <= 10)
            .WithMessage("Maximum 10 extras allowed");

        RuleForEach(x => x.Extras)
            .Must(e => ValidExtras.Contains(e))
            .WithMessage("Invalid extra: {PropertyValue}");
    }
}
