// Same prompt/dependencies as complex-email-sender-05 — this case is the repair-loop variant
// guarding the CS0103 try-scoped-variable bug Phase 0 found on the retry-once pattern.
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
