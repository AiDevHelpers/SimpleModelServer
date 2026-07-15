// Stub for complex-background-service-07: IOutboxRepository/IMessageBus/OutboxMessage are the
// "assume exist" dependencies. BackgroundService, ILogger, and PeriodicTimer are real framework
// types (Microsoft.AspNetCore.App shared framework / BCL) and need no stub.
namespace DraftModel.Evals.Stubs;

public sealed class OutboxMessage
{
    public Guid Id { get; set; }
    public string Payload { get; set; } = "";
}

public interface IOutboxRepository
{
    Task<IReadOnlyList<OutboxMessage>> GetUnpublishedAsync(CancellationToken cancellationToken);

    Task MarkPublishedAsync(Guid id, CancellationToken cancellationToken);
}

public interface IMessageBus
{
    Task PublishAsync(OutboxMessage message, CancellationToken cancellationToken);
}
