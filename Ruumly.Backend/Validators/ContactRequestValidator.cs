using FluentValidation;
using Ruumly.Backend.DTOs.Requests;

namespace Ruumly.Backend.Validators;

public class ContactRequestValidator : AbstractValidator<ContactRequest>
{
    public ContactRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Name is required.")
            .MaximumLength(120).WithMessage("Name must not exceed 120 characters.");

        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required.")
            .EmailAddress().WithMessage("Invalid email address.")
            .MaximumLength(200).WithMessage("Email must not exceed 200 characters.");

        RuleFor(x => x.Subject)
            .NotEmpty().WithMessage("Subject is required.")
            .MaximumLength(200).WithMessage("Subject must not exceed 200 characters.");

        RuleFor(x => x.Message)
            .NotEmpty().WithMessage("Message is required.")
            .MinimumLength(10).WithMessage("Message must be at least 10 characters.")
            .MaximumLength(5000).WithMessage("Message must not exceed 5000 characters.");
    }
}
