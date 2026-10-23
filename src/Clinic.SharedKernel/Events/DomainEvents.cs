using Microsoft.Extensions.DependencyInjection;

namespace Clinic.SharedKernel.Events;

/// <summary>
/// In-process domain event, dispatched synchronously inside the publisher's unit of work so that
/// subscribers (higher-level modules, ADR-002) take part in the same database transaction.
/// </summary>
public interface IDomainEvent;

public interface IDomainEventHandler<in TEvent> where TEvent : IDomainEvent
{
    Task HandleAsync(TEvent domainEvent, CancellationToken cancellationToken);
}

public interface IDomainEventDispatcher
{
    Task PublishAsync<TEvent>(TEvent domainEvent, CancellationToken cancellationToken) where TEvent : IDomainEvent;
}

public sealed class DomainEventDispatcher(IServiceProvider services) : IDomainEventDispatcher
{
    public async Task PublishAsync<TEvent>(TEvent domainEvent, CancellationToken cancellationToken)
        where TEvent : IDomainEvent
    {
        foreach (var handler in services.GetServices<IDomainEventHandler<TEvent>>())
        {
            await handler.HandleAsync(domainEvent, cancellationToken);
        }
    }
}
