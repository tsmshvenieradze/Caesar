using Caesar.DependencyInjection;
using Caesar.NotificationPublishers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Caesar.Docs.Snippets;

public static class ConfigurationExamples
{
    public static void AllOptions(IServiceCollection services)
    {
        #region all-options
        services.AddCaesar(cfg =>
        {
            cfg.RegisterServicesFromAssemblyContaining<CreateCustomer>();       // required, at least one assembly
            cfg.TypeEvaluator = type => type.Namespace?.StartsWith("Contoso.Application", StringComparison.Ordinal) == true;

            cfg.Lifetime = ServiceLifetime.Transient;                            // scanned handlers, processors, exception handlers
            cfg.MediatorLifetime = ServiceLifetime.Scoped;                       // IMediator, ISender, IPublisher, INotificationPublisher
            cfg.MediatorImplementationType = typeof(Mediator);
            cfg.NotificationPublisherType = typeof(ForeachAwaitPublisher);
            cfg.AutoRegisterRequestProcessors = true;
            cfg.RequestExceptionActionProcessorStrategy = RequestExceptionActionProcessorStrategy.ApplyForUnhandledExceptions;
            cfg.BypassExceptionHandlingOnCallerCancellation = false;
        });
        #endregion
    }

    public static async Task Scope(IHost host)
    {
        #region scope
        using var scope = host.Services.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        await sender.Send(new DeactivateCustomer(Guid.NewGuid()));
        #endregion
    }

    public static void TypeEvaluator(IServiceCollection services)
    {
        #region type-evaluator
        services.AddCaesar(cfg =>
        {
            cfg.RegisterServicesFromAssemblyContaining<CreateCustomer>();
            cfg.TypeEvaluator = type => type != typeof(CountCustomers);   // keep this handler out of the scan
        });
        #endregion
    }
}

#region background-service
// A BackgroundService is a singleton: open a scope per unit of work instead of injecting ISender.
public sealed class NightlyExport(IServiceScopeFactory scopeFactory) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await using (var scope = scopeFactory.CreateAsyncScope())
            {
                var sender = scope.ServiceProvider.GetRequiredService<ISender>();
                await foreach (var page in sender.CreateStream(new ExportCustomers(500), stoppingToken))
                {
                    Console.WriteLine($"Exported {page.Count}");
                }
            }

            await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
        }
    }
}
#endregion

#region open-generic-handler
// Registered against INotificationHandler<> and closed by the container for every notification type.
public sealed class AuditEverything<TNotification> : INotificationHandler<TNotification>
    where TNotification : INotification
{
    public Task Handle(TNotification notification, CancellationToken cancellationToken)
    {
        Console.WriteLine($"Published {typeof(TNotification).Name}");
        return Task.CompletedTask;
    }
}
#endregion
