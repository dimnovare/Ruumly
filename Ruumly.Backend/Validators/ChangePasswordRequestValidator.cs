using FluentValidation;
using Ruumly.Backend.DTOs.Requests;

namespace Ruumly.Backend.Validators;

public class ChangePasswordRequestValidator : AbstractValidator<ChangePasswordRequest>
{
    public ChangePasswordRequestValidator()
    {
        RuleFor(x => x.NewPassword)
            .NotEmpty()
            .MinimumLength(8)
            .WithMessage("Parool peab olema vähemalt 8 tähemärki.");

        RuleFor(x => x.ConfirmPassword)
            .NotEmpty()
            .Equal(x => x.NewPassword)
            .WithMessage("Paroolid ei ühti.");
    }
}
