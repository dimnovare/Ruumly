namespace Ruumly.Backend.DTOs.Responses;

public sealed record OutreachPreviewItemDto(
    Guid SupplierId,
    string? SupplierName,
    string? Email,
    string? Language,
    string? Subject,
    string? TextBody,
    string? SkipReason);

public sealed record OutreachPreviewResponse(IReadOnlyList<OutreachPreviewItemDto> Recipients);

public sealed record ProviderOutreachMessage(
    string Language, string Subject, string TextBody);
