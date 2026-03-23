namespace Ruumly.Backend.DTOs.Requests;

public record UpdateBankDetailsRequest(
    string? Iban,
    string? BankAccountName,
    string? BankName
);
