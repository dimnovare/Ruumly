using Ruumly.Backend.Models.Enums;

namespace Ruumly.Backend.DTOs.Responses;

public record UserDto(
    Guid Id,
    string Name,
    string Email,
    UserRole Role,
    UserStatus Status,
    string? Company,
    string? Phone,
    string? Avatar,
    DateTime RegisteredAt,
    DateTime? LastLoginAt,
    int BookingsCount,
    bool HasGoogleAccount
);
