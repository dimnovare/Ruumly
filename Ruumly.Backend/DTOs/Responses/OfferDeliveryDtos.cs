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

public sealed record PublicOfferLeadDto(
    string Category, string City, string? ToCity, DateTime? NeedDate, string? Details);

public sealed record PublicOfferOptionDto(
    Guid Id, string Title, decimal? PriceAmount, string? PriceUnit,
    string? Notes, string? SupplierName);

public sealed record PublicOfferDto(
    string Status, string Language, string? CustomerNote, DateTime? SentAt,
    Guid? ChosenOptionId, PublicOfferLeadDto? Lead,
    IReadOnlyList<PublicOfferOptionDto> Options);

public sealed record OfferDeliveryRecipientDto(string? Name, string Email);
public sealed record OfferDeliveryEmailDto(string Subject, string TextBody);
public sealed record OfferDeliveryPreviewDto(
    OfferDeliveryRecipientDto Recipient,
    OfferDeliveryEmailDto Email,
    PublicOfferDto Page);
public sealed record OfferEmailMessage(string Subject, string TextBody, string Link);
