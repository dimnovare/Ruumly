namespace Ruumly.Backend.Services.Interfaces;

public interface IBackgroundEmailQueue
{
    void EnqueueEmail(string to, string subject, string textBody, string? htmlBody = null);
    void EnqueueVerificationEmail(Guid userId);
}
