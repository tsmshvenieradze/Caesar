using Microsoft.Extensions.Logging;

namespace Caesar.Sample.Application.Customers;

public sealed record CustomerCreated(Guid CustomerId, string Email) : INotification;

public sealed class SendWelcomeEmail(ILogger<SendWelcomeEmail> logger) : INotificationHandler<CustomerCreated>
{
    public Task Handle(CustomerCreated notification, CancellationToken cancellationToken)
    {
        logger.LogInformation("Welcome email queued for {Email}", notification.Email);
        return Task.CompletedTask;
    }
}

public sealed class UpdateCrm(ILogger<UpdateCrm> logger) : INotificationHandler<CustomerCreated>
{
    public Task Handle(CustomerCreated notification, CancellationToken cancellationToken)
    {
        logger.LogInformation("CRM updated for customer {CustomerId}", notification.CustomerId);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Handles every notification: <see cref="INotificationHandler{TNotification}"/> is contravariant, so Caesar also runs
/// the handlers registered for a notification's base classes and interfaces, after those for its own type.
/// </summary>
public sealed class AuditNotifications(ILogger<AuditNotifications> logger) : INotificationHandler<INotification>
{
    public Task Handle(INotification notification, CancellationToken cancellationToken)
    {
        logger.LogInformation("Audit: {Notification} published", notification.GetType().Name);
        return Task.CompletedTask;
    }
}
