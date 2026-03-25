namespace Ruumly.Backend.DTOs.Responses;

/// <summary>
/// Public review DTO — never exposes UserId or email.
/// </summary>
public record ReviewDto(
    Guid     Id,
    int      Rating,
    string?  Comment,
    string   UserName,
    string   CreatedAt
);
