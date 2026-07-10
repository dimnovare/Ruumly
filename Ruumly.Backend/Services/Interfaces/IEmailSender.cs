namespace Ruumly.Backend.Services.Interfaces;

public interface IEmailSender
{
    Task SendAsync(
        string to,
        string subject,
        string textBody,
        string? htmlBody = null);

    /// <summary>
    /// Send with a Reply-To header (provider outreach: replies must land in
    /// the ops inbox, not the noreply sender). Default implementation drops
    /// the Reply-To so existing fakes/implementations keep compiling — real
    /// senders override it.
    /// </summary>
    Task SendAsync(
        string to,
        string subject,
        string textBody,
        string? htmlBody,
        string? replyTo)
        => SendAsync(to, subject, textBody, htmlBody);
}
