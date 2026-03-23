namespace Ruumly.Backend.DTOs.Requests;

public record ResetPasswordRequest(string Token, string NewPassword);
