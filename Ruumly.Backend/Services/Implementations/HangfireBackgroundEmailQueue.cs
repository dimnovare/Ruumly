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

    // Separate Hangfire method (not an extra parameter on SendAsync): jobs
    // already serialized with the 4-arg signature must still resolve after a
    // deploy — changing SendAsync's arity would permanently fail them.
    public void EnqueueEmail(string to, string subject, string textBody, string? htmlBody, string? replyTo)
    {
        if (string.IsNullOrWhiteSpace(replyTo))
        {
            EnqueueEmail(to, subject, textBody, htmlBody);
            return;
        }

        jobs.Enqueue<BackgroundEmailService>(
            service => service.SendWithReplyToAsync(to, subject, textBody, htmlBody, replyTo));
    }

    public void EnqueueVerificationEmail(Guid userId)
    {
        jobs.Enqueue<BackgroundEmailService>(
            service => service.SendVerificationEmailAsync(userId));
    }
}
