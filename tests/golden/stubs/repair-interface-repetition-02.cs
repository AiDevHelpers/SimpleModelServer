// Same prompt/dependencies as complex-email-sender-05 — this case is the repair-loop variant
// guarding the "using System.Xml.Linq;" repetition-loop failure Phase 0 found on this exact prompt.
namespace DraftModel.Evals.Stubs;

public enum NotificationChannel
{
    Email,
    Sms,
    Push
}

public sealed class NotificationMessage
{
    public string TemplateId { get; set; } = "";
    public string Recipient { get; set; } = "";
}

public interface INotificationSender
{
    Task SendAsync(NotificationMessage message, CancellationToken cancellationToken = default);

    bool CanHandle(NotificationChannel channel);
}

public interface ISmtpClient
{
    Task SendAsync(string to, string subject, string body, CancellationToken cancellationToken);
}

public interface ITemplateRenderer
{
    Task<string> RenderAsync(string templateId, CancellationToken cancellationToken);
}
