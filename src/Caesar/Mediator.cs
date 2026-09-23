using System.Collections.Concurrent;
using Caesar.Wrappers;

namespace Caesar;

/// <summary>
/// Default <see cref="IMediator"/> implementation. Resolves handlers and behaviors from an
/// <see cref="IServiceProvider"/> at call time, so it can be registered with any lifetime.
/// Handler wrappers are stateless and cached per request type for the lifetime of the process.
/// </summary>
public class Mediator : IMediator
{
    private static readonly ConcurrentDictionary<Type, RequestHandlerBase> RequestHandlers = new();
    private static readonly ConcurrentDictionary<Type, NotificationHandlerWrapper> NotificationHandlers = new();
    private static readonly ConcurrentDictionary<Type, StreamRequestHandlerBase> StreamRequestHandlers = new();

    private readonly IServiceProvider _serviceProvider;
    private readonly INotificationPublisher _publisher;
    private readonly Func<IEnumerable<NotificationHandlerExecutor>, INotification, CancellationToken, Task> _publishCore;

    /// <summary>
    /// Creates a mediator that publishes notifications sequentially (<see cref="NotificationPublishers.ForeachAwaitPublisher"/>).
    /// </summary>
    /// <param name="serviceProvider">Provider used to resolve handlers and behaviors.</param>
    public Mediator(IServiceProvider serviceProvider)
        : this(serviceProvider, new NotificationPublishers.ForeachAwaitPublisher())
    {
    }

    /// <summary>
    /// Creates a mediator with a custom notification publishing strategy.
    /// </summary>
    /// <param name="serviceProvider">Provider used to resolve handlers and behaviors.</param>
    /// <param name="publisher">Strategy used to invoke notification handlers.</param>
    public Mediator(IServiceProvider serviceProvider, INotificationPublisher publisher)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
        _publishCore = PublishCore;
    }

    /// <inheritdoc />
    public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var handler = (RequestHandlerWrapper<TResponse>)RequestHandlers.GetOrAdd(request.GetType(), static requestType =>
        {
            // A command (IRequest) is dispatched to IRequestHandler<TRequest>; everything else to IRequestHandler<TRequest, TResponse>.
            var wrapperType = typeof(IRequest).IsAssignableFrom(requestType) && typeof(TResponse) == typeof(Unit)
                ? typeof(RequestHandlerWrapperImpl<>).MakeGenericType(requestType)
                : typeof(RequestHandlerWrapperImpl<,>).MakeGenericType(requestType, typeof(TResponse));

            return CreateWrapper<RequestHandlerBase>(wrapperType, requestType);
        });

        return handler.Handle(request, _serviceProvider, cancellationToken);
    }

    /// <inheritdoc />
    public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
        where TRequest : IRequest
    {
        ArgumentNullException.ThrowIfNull(request);

        var handler = (RequestHandlerWrapper<Unit>)RequestHandlers.GetOrAdd(request.GetType(), static requestType =>
            CreateWrapper<RequestHandlerBase>(typeof(RequestHandlerWrapperImpl<>).MakeGenericType(requestType), requestType));

        return handler.Handle(request, _serviceProvider, cancellationToken);
    }

    /// <inheritdoc />
    public Task<object?> Send(object request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var handler = RequestHandlers.GetOrAdd(request.GetType(), static requestType =>
        {
            if (typeof(IRequest).IsAssignableFrom(requestType))
            {
                return CreateWrapper<RequestHandlerBase>(typeof(RequestHandlerWrapperImpl<>).MakeGenericType(requestType), requestType);
            }

            var requestInterface = FindClosedInterface(requestType, typeof(IRequest<>))
                ?? throw new ArgumentException($"{requestType.FullName} does not implement {typeof(IRequest<>).FullName}.", nameof(request));

            var responseType = requestInterface.GetGenericArguments()[0];
            return CreateWrapper<RequestHandlerBase>(typeof(RequestHandlerWrapperImpl<,>).MakeGenericType(requestType, responseType), requestType);
        });

        return handler.Handle(request, _serviceProvider, cancellationToken);
    }

    /// <inheritdoc />
    public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
        where TNotification : INotification
    {
        ArgumentNullException.ThrowIfNull(notification);
        return PublishNotification(notification, cancellationToken);
    }

    /// <inheritdoc />
    public Task Publish(object notification, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(notification);

        return notification is INotification instance
            ? PublishNotification(instance, cancellationToken)
            : throw new ArgumentException($"{notification.GetType().FullName} does not implement {typeof(INotification).FullName}.", nameof(notification));
    }

    /// <inheritdoc />
    public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var handler = (StreamRequestHandlerWrapper<TResponse>)StreamRequestHandlers.GetOrAdd(request.GetType(), static requestType =>
            CreateWrapper<StreamRequestHandlerBase>(typeof(StreamRequestHandlerWrapperImpl<,>).MakeGenericType(requestType, typeof(TResponse)), requestType));

        return handler.Handle(request, _serviceProvider, cancellationToken);
    }

    /// <inheritdoc />
    public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var handler = StreamRequestHandlers.GetOrAdd(request.GetType(), static requestType =>
        {
            var requestInterface = FindClosedInterface(requestType, typeof(IStreamRequest<>))
                ?? throw new ArgumentException($"{requestType.FullName} does not implement {typeof(IStreamRequest<>).FullName}.", nameof(request));

            var responseType = requestInterface.GetGenericArguments()[0];
            return CreateWrapper<StreamRequestHandlerBase>(typeof(StreamRequestHandlerWrapperImpl<,>).MakeGenericType(requestType, responseType), requestType);
        });

        return handler.Handle(request, _serviceProvider, cancellationToken);
    }

    /// <summary>
    /// Publishes a notification through the configured <see cref="INotificationPublisher"/>.
    /// Override to intercept every publish (for example to add logging or tracing).
    /// </summary>
    /// <param name="handlerExecutors">The resolved handler executors.</param>
    /// <param name="notification">The notification.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    protected virtual Task PublishCore(IEnumerable<NotificationHandlerExecutor> handlerExecutors, INotification notification, CancellationToken cancellationToken)
        => _publisher.Publish(handlerExecutors, notification, cancellationToken);

    private Task PublishNotification(INotification notification, CancellationToken cancellationToken)
    {
        var handler = NotificationHandlers.GetOrAdd(notification.GetType(), static notificationType =>
            CreateWrapper<NotificationHandlerWrapper>(typeof(NotificationHandlerWrapperImpl<>).MakeGenericType(notificationType), notificationType));

        return handler.Handle(notification, _serviceProvider, _publishCore, cancellationToken);
    }

    private static TWrapper CreateWrapper<TWrapper>(Type wrapperType, Type messageType)
        where TWrapper : class
        => (TWrapper?)Activator.CreateInstance(wrapperType)
           ?? throw new InvalidOperationException($"Could not create wrapper type for {messageType.FullName}.");

    private static Type? FindClosedInterface(Type type, Type openGenericInterface)
    {
        foreach (var candidate in type.GetInterfaces())
        {
            if (candidate.IsGenericType && candidate.GetGenericTypeDefinition() == openGenericInterface)
            {
                return candidate;
            }
        }

        return null;
    }
}
