namespace Ruumly.Backend.DTOs.Requests;

public record AdminPatchUserRequest(
    string? Name          = null,
    string? Phone         = null,
    string? Company       = null,
    string? Role          = null,
    string? Status        = null,
    bool?   EmailVerified = null,
    Guid?   SupplierId    = null
);
