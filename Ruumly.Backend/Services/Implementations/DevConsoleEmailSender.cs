using Ruumly.Backend.Services.Interfaces;

namespace Ruumly.Backend.Services.Implementations;

public class DevConsoleEmailSender(ILogger<DevConsoleEmailSender> logger) : IEmailSender
{
    public Task SendAsync(string to, string subject, string textBody, string? htmlBody = null)
    {
        logger.LogInformation(
            "[DEV EMAIL] To: {To} | Subject: {Subject}\n{Body}",
            to, subject, textBody);
        return Task.CompletedTask;
    }
}
