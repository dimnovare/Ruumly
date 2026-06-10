using Hangfire;
using Ruumly.Backend.Services.Interfaces;

namespace Ruumly.Backend.Services.Implementations;

public class HangfireBackgroundEmailQueue(IBackgroundJobClient jobs) : IBackgroundEmailQueue
{
    public void EnqueueEmail(string to, string subject, string textBody, string? htmlBody = null)
    {
        jobs.Enqueue<BackgroundEmailService>(
            service => service.SendAsync(to, subject, textBody, htmlBody));
    }

    public void EnqueueVerificationEmail(Guid userId)
    {
        jobs.Enqueue<BackgroundEmailService>(
            service => service.SendVerificationEmailAsync(userId));
    }
}
