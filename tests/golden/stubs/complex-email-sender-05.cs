// Stub for complex-email-sender-05: INotificationSender is given verbatim in the prompt text but
// the model's extracted code block may or may not re-declare it, so it's stubbed here too to avoid
// a false compile failure either way. ISmtpClient/ITemplateRenderer/NotificationMessage/
// NotificationChannel are the "assume exist" dependencies named in the prompt. SmtpException is the
// real System.Net.Mail type — no stub needed for it.
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
