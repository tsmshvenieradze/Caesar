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
