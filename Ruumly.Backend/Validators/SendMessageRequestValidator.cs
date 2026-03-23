using FluentValidation;
using Ruumly.Backend.DTOs.Requests;

namespace Ruumly.Backend.Validators;

public class SendMessageRequestValidator : AbstractValidator<SendMessageRequest>
{
    public SendMessageRequestValidator()
    {
        RuleFor(x => x.Text)
            .NotEmpty().WithMessage("Sõnumi tekst on kohustuslik")
            .MaximumLength(2000).WithMessage("Sõnum ei tohi olla pikem kui 2000 tähemärki");
    }
}
