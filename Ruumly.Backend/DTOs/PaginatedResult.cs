namespace Ruumly.Backend.DTOs;

public record PaginatedResult<T>(
    List<T> Data,
    int Total,
    int Page,
    int Limit,
    bool HasMore
);
