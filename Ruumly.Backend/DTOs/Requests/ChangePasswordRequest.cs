namespace Ruumly.Backend.DTOs.Requests;

public record ChangePasswordRequest(
    string CurrentPassword,
    string NewPassword,
    string ConfirmPassword
);
