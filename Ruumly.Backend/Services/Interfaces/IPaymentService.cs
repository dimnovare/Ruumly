namespace Ruumly.Backend.Services.Interfaces;

public interface IPaymentService
{
    /// <summary>
    /// Creates a Montonio payment order and returns
    /// the URL to redirect the customer to.
    /// Returns empty string for "pay later".
    /// </summary>
    Task<string> CreatePaymentOrderAsync(
        Guid invoiceId,
        string paymentMethod,
        string customerEmail,
        string customerLocale);

    /// <summary>
    /// Verifies a Montonio webhook JWT and marks
    /// the invoice as paid if valid.
    /// </summary>
    Task<bool> HandleWebhookAsync(string token);
}
