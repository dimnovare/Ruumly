namespace Ruumly.Backend.DTOs;

public record PaginatedResult<T>(
    List<T> Data,
    int Total,
    int Page,
    int Limit,
    bool HasMore
)
{
    /// <summary>Returns an empty page — used as an early-exit when input is invalid.</summary>
    public static PaginatedResult<T> Empty(int page, int limit) =>
        new([], 0, page, limit, false);
}
