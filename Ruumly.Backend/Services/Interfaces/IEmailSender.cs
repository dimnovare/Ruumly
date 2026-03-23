namespace Ruumly.Backend.Services.Interfaces;

public interface IEmailSender
{
    Task SendAsync(
        string to,
        string subject,
        string textBody,
        string? htmlBody = null);
}
