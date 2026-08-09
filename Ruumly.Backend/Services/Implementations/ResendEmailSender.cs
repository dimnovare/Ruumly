using Resend;
using Ruumly.Backend.Helpers;
using Ruumly.Backend.Services.Interfaces;

namespace Ruumly.Backend.Services.Implementations;

public class ResendEmailSender(
    IResend resend,
    IConfiguration config,
    ILogger<ResendEmailSender> logger) : IEmailSender
{
    // Helpers/EmailFrom is the single source of truth: the supplier-intro dry
    // run previews the From line from there, and a preview that disagrees with
    // the real sender is worse than no preview.
    private string From => EmailFrom.Render(config);

    public Task SendAsync(string to, string subject, string textBody, string? htmlBody = null)
        => SendAsync(to, subject, textBody, htmlBody, replyTo: null);

    public async Task SendAsync(string to, string subject, string textBody, string? htmlBody, string? replyTo)
    {
        var message = new EmailMessage();
        message.From    = From;
        message.To.Add(to);
        message.Subject  = subject;
        message.TextBody = textBody;
        if (!string.IsNullOrWhiteSpace(htmlBody))
            message.HtmlBody = htmlBody;
        if (!string.IsNullOrWhiteSpace(replyTo))
            message.ReplyTo = replyTo;   // implicit string → EmailAddressList

        try
        {
            await resend.EmailSendAsync(message);
            logger.LogDebug("Email sent to {To}: {Subject}", to, subject);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Failed to send email to {To} with subject {Subject}", to, subject);
            throw;  // re-throw so callers know it failed and Hangfire can retry
        }
    }
}
