using Ruumly.Backend.Services.Interfaces;

namespace Ruumly.Backend.Services.Implementations;

public class DevConsoleEmailSender(ILogger<DevConsoleEmailSender> logger) : IEmailSender
{
    public Task SendAsync(string to, string subject, string textBody, string? htmlBody = null)
        => SendAsync(to, subject, textBody, htmlBody, replyTo: null);

    public Task SendAsync(string to, string subject, string textBody, string? htmlBody, string? replyTo)
    {
        logger.LogInformation(
            "[DEV EMAIL] To: {To} | Reply-To: {ReplyTo} | Subject: {Subject}\n{Body}",
            to, replyTo ?? "-", subject, textBody);
        return Task.CompletedTask;
    }
}
