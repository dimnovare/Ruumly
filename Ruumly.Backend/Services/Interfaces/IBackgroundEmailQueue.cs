namespace Ruumly.Backend.Services.Interfaces;

public interface IBackgroundEmailQueue
{
    void EnqueueEmail(string to, string subject, string textBody, string? htmlBody = null);

    /// <summary>
    /// Enqueue with a Reply-To header (provider outreach: replies must land
    /// in the ops inbox, not the noreply sender). Default implementation
    /// drops the Reply-To so existing fakes keep compiling — the real queue
    /// overrides it.
    /// </summary>
    void EnqueueEmail(string to, string subject, string textBody, string? htmlBody, string? replyTo)
        => EnqueueEmail(to, subject, textBody, htmlBody);

    /// <summary>
    /// Enqueue for delivery after <paramref name="delay"/> — used to pace bulk
    /// campaigns (supplier introduction) so hundreds of recipients drip out
    /// instead of bursting past Resend's rate limit and into spam heuristics.
    /// Default implementation sends immediately so existing fakes keep
    /// compiling; the real queue schedules it.
    /// </summary>
    void EnqueueEmailAfter(
        TimeSpan delay, string to, string subject, string textBody, string? htmlBody, string? replyTo)
        => EnqueueEmail(to, subject, textBody, htmlBody, replyTo);

    void EnqueueVerificationEmail(Guid userId);
}
