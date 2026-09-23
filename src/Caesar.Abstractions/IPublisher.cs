namespace Caesar;

/// <summary>
/// Publishes notifications to every registered handler.
/// </summary>
public interface IPublisher
{
    /// <summary>Publishes a notification to all of its handlers using the configured <see cref="INotificationPublisher"/>.</summary>
    /// <typeparam name="TNotification">The notification type.</typeparam>
    /// <param name="notification">The notification.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
        where TNotification : INotification;

    /// <summary>
    /// Publishes a notification whose type is only known at runtime. The object must implement <see cref="INotification"/>.
    /// </summary>
    /// <param name="notification">The notification.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task Publish(object notification, CancellationToken cancellationToken = default);
}
