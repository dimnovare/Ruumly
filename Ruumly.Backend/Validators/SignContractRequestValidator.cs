using FluentValidation;
using Ruumly.Backend.DTOs.Requests;

namespace Ruumly.Backend.Validators;

public class SignContractRequestValidator : AbstractValidator<SignContractRequest>
{
    public SignContractRequestValidator()
    {
        RuleFor(x => x.BookingId)
            .NotEmpty().WithMessage("BookingId is required.");

        RuleFor(x => x.ContractTemplateId)
            .NotEmpty().WithMessage("ContractTemplateId is required.");

        RuleFor(x => x.TenantName)
            .NotEmpty().WithMessage("Full name is required.")
            .MaximumLength(200).WithMessage("Full name must be 200 characters or fewer.");

        RuleFor(x => x.TenantIdCode)
            .MaximumLength(20).WithMessage("ID code must be 20 characters or fewer.")
            .When(x => x.TenantIdCode is not null);
    }
}
