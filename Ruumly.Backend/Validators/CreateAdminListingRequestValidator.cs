using FluentValidation;
using Ruumly.Backend.DTOs.Requests;

namespace Ruumly.Backend.Validators;

public class CreateAdminListingRequestValidator : AbstractValidator<CreateAdminListingRequest>
{
    private static readonly string[] ValidTypes =
        ["warehouse", "moving", "trailer"];

    public CreateAdminListingRequestValidator()
    {
        RuleFor(x => x.Title)
            .NotEmpty().MaximumLength(200);

        RuleFor(x => x.Type)
            .NotEmpty()
            .Must(t => ValidTypes.Contains(t.ToLower()))
            .WithMessage("Type must be warehouse, moving, or trailer");

        RuleFor(x => x.City)
            .NotEmpty().MaximumLength(100);

        RuleFor(x => x.PriceFrom)
            .GreaterThan(0).WithMessage("Price must be greater than 0")
            .LessThan(100_000);

        RuleFor(x => x.Description)
            .MaximumLength(5000).When(x => x.Description != null);

        RuleFor(x => x.SupplierId)
            .NotEmpty().WithMessage("SupplierId is required");

        RuleFor(x => x.PartnerDiscountRateOverride)
            .InclusiveBetween(0, 80)
            .When(x => x.PartnerDiscountRateOverride.HasValue);

        RuleFor(x => x.ClientDiscountRateOverride)
            .InclusiveBetween(0, 80)
            .When(x => x.ClientDiscountRateOverride.HasValue);

        RuleFor(x => x.VatRate)
            .InclusiveBetween(0, 30)
            .When(x => x.VatRate.HasValue);
    }
}
