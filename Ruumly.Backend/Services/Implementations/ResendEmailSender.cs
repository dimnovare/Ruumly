using Resend;
using Ruumly.Backend.Services.Interfaces;

namespace Ruumly.Backend.Services.Implementations;

public class ResendEmailSender(IResend resend, IConfiguration config) : IEmailSender
{
    private string From =>
        $"{config["Email:FromName"] ?? "Ruumly"} <{config["Email:FromAddress"] ?? "noreply@ruumly.eu"}>";

    public async Task SendAsync(string to, string subject, string textBody, string? htmlBody = null)
    {
        var message = new EmailMessage();
        message.From = From;
        message.To.Add(to);
        message.Subject = subject;
        message.TextBody = textBody;
        if (!string.IsNullOrWhiteSpace(htmlBody))
            message.HtmlBody = htmlBody;

        await resend.EmailSendAsync(message);
    }
}
