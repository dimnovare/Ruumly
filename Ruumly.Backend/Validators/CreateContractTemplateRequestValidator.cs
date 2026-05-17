using FluentValidation;
using Ruumly.Backend.DTOs.Requests;

namespace Ruumly.Backend.Validators;

public class CreateContractTemplateRequestValidator
    : AbstractValidator<CreateContractTemplateRequest>
{
    public CreateContractTemplateRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty()
            .MaximumLength(200);

        RuleFor(x => x.Html)
            .NotEmpty()
            .MaximumLength(500_000)
            .WithMessage("Contract HTML must not exceed 500 KB.");
    }
}
