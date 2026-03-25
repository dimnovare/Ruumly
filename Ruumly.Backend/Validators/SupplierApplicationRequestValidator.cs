using FluentValidation;
using Ruumly.Backend.DTOs.Requests;

namespace Ruumly.Backend.Validators;

public class SupplierApplicationRequestValidator : AbstractValidator<SupplierApplicationRequest>
{
    public SupplierApplicationRequestValidator()
    {
        RuleFor(x => x.CompanyName)
            .NotEmpty().WithMessage("Company name is required.")
            .MaximumLength(200).WithMessage("Company name must not exceed 200 characters.");

        RuleFor(x => x.RegistryCode)
            .NotEmpty().WithMessage("Registry code is required.")
            .Matches(@"^\d{8}$").WithMessage("Registry code must be exactly 8 digits.");

        RuleFor(x => x.ContactName)
            .NotEmpty().WithMessage("Contact name is required.")
            .MaximumLength(100).WithMessage("Contact name must not exceed 100 characters.");

        RuleFor(x => x.ContactEmail)
            .NotEmpty().WithMessage("Contact email is required.")
            .EmailAddress().WithMessage("Invalid email address.");

        RuleFor(x => x.ContactPhone)
            .NotEmpty().WithMessage("Contact phone is required.")
            .MaximumLength(30).WithMessage("Contact phone must not exceed 30 characters.");

        RuleFor(x => x.BusinessType)
            .NotEmpty().WithMessage("Business type is required.")
            .MaximumLength(100).WithMessage("Business type must not exceed 100 characters.");

        RuleFor(x => x.ServiceTypes)
            .NotNull().WithMessage("Service types are required.")
            .Must(s => s.Length > 0).WithMessage("At least one service type is required.");

        RuleFor(x => x.ServiceAreas)
            .NotNull().WithMessage("Service areas are required.")
            .Must(s => s.Length > 0).WithMessage("At least one service area is required.");

        RuleFor(x => x.Notes)
            .MaximumLength(1000).WithMessage("Notes must not exceed 1000 characters.")
            .When(x => x.Notes is not null);
    }
}
