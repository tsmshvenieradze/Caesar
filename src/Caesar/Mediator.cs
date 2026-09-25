using System.Runtime.ExceptionServices;
using Caesar.Wrappers;

namespace Caesar;

/// <summary>
/// Default <see cref="IMediator"/> implementation. Resolves handlers and behaviors from an
/// <see cref="IServiceProvider"/> at call time, so it can be registered with any lifetime.
/// Handler wrappers are stateless and cached per request type for the lifetime of the process, except for request
/// types from a collectible assembly, whose entries go away when that assembly is unloaded.
/// </summary>
public class Mediator : IMediator
{
    // The typed overloads keep one cache per call-site response type (RequestWrappers<TResponse> and
    // StreamWrappers<TResponse>), so a call made with one TResponse can never hand another call site a wrapper of the
    // wrong type. The object overloads have caches of their own, built from the request's declared interface.
    private static readonly TypeKeyedCache<RequestHandlerBase> RequestHandlers = new();
    private static readonly TypeKeyedCache<NotificationHandlerWrapper> NotificationHandlers = new();
    private static readonly TypeKeyedCache<StreamRequestHandlerBase> StreamRequestHandlers = new();

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
    /// <remarks>
    /// Failures are reported through the returned task, never thrown from this call, except
    /// <see cref="ArgumentNullException"/> and <see cref="ArgumentException"/> for a request Caesar cannot dispatch.
    /// </remarks>
    public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var handler = RequestWrappers<TResponse>.Get(request.GetType());

        try
        {
            return handler.Handle(request, _serviceProvider, cancellationToken);
        }
#pragma warning disable CA1031 // Not handled: every failure is handed to the caller through the returned task.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            return FromException<TResponse>(exception);
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Failures are reported through the returned task, never thrown from this call, except
    /// <see cref="ArgumentNullException"/> for a <see langword="null"/> request.
    /// </remarks>
    public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
        where TRequest : IRequest
    {
        ArgumentNullException.ThrowIfNull(request);

        // The same wrapper Send<Unit>(IRequest<Unit>) uses, so a command behaves the same through every overload.
        var handler = RequestWrappers<Unit>.Get(request.GetType());

        try
        {
            return handler.Handle(request, _serviceProvider, cancellationToken);
        }
#pragma warning disable CA1031 // Not handled: every failure is handed to the caller through the returned task.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            return FromException<Unit>(exception);
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Failures are reported through the returned task, never thrown from this call, except
    /// <see cref="ArgumentNullException"/> for a <see langword="null"/> request and <see cref="ArgumentException"/> for
    /// an object that does not declare exactly one <see cref="IRequest{TResponse}"/>.
    /// </remarks>
    public Task<object?> Send(object request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var handler = RequestHandlers.GetOrAdd(request.GetType(), static requestType =>
        {
            var responseType = DeclaredResponseType(requestType, typeof(IRequest<>), nameof(request));
            return CreateRequestWrapper(requestType, responseType);
        });

        // Every wrapper's object overload is an async method, so it cannot throw from here.
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
    /// <remarks>Nothing is resolved or run until the stream is enumerated, so failures surface from the enumerator.</remarks>
    public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var handler = StreamWrappers<TResponse>.Get(request.GetType());
        return handler.Handle(request, _serviceProvider, cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Nothing is resolved or run until the stream is enumerated. An object that does not declare exactly one
    /// <see cref="IStreamRequest{TResponse}"/> is rejected with <see cref="ArgumentException"/> straight away.
    /// </remarks>
    public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var handler = StreamRequestHandlers.GetOrAdd(request.GetType(), static requestType =>
        {
            var responseType = DeclaredResponseType(requestType, typeof(IStreamRequest<>), nameof(request));
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

    /// <summary>
    /// Completes a task the way an async method completes when it throws <paramref name="exception"/>: faulted, or
    /// canceled for an <see cref="OperationCanceledException"/>, carrying the original exception and stack trace.
    /// </summary>
#pragma warning disable CS1998 // No await on purpose: the async builder turns the rethrow into a faulted or canceled task.
    private static async Task<TResponse> FromException<TResponse>(Exception exception)
    {
        ExceptionDispatchInfo.Throw(exception);
        return default!;
    }
#pragma warning restore CS1998

    /// <summary>The wrapper for a request dispatched to <c>IRequestHandler&lt;TRequest, TResponse&gt;</c>, or to a command handler.</summary>
    private static RequestHandlerBase CreateRequestWrapper(Type requestType, Type responseType)
    {
        // A command (IRequest) goes to IRequestHandler<TRequest>, falling back to IRequestHandler<TRequest, Unit>.
        var wrapperType = responseType == typeof(Unit) && typeof(IRequest).IsAssignableFrom(requestType)
            ? typeof(RequestHandlerWrapperImpl<>).MakeGenericType(requestType)
            : typeof(RequestHandlerWrapperImpl<,>).MakeGenericType(requestType, responseType);

        return CreateWrapper<RequestHandlerBase>(wrapperType, requestType);
    }

    private static TWrapper CreateWrapper<TWrapper>(Type wrapperType, Type messageType, params object[] arguments)
        where TWrapper : class
        => (TWrapper?)Activator.CreateInstance(wrapperType, arguments)
           ?? throw new InvalidOperationException($"Could not create wrapper type for {messageType.FullName}.");

    /// <summary>The response type of the single <paramref name="openInterface"/> that <paramref name="requestType"/> declares.</summary>
    /// <exception cref="ArgumentException">The type declares none, or several; the object overloads cannot choose.</exception>
    private static Type DeclaredResponseType(Type requestType, Type openInterface, string paramName)
    {
        var declared = FindClosedInterfaces(requestType, openInterface);

        return declared.Count switch
        {
            1 => declared[0].GenericTypeArguments[0],
            0 => throw new ArgumentException($"{TypeNames.Of(requestType, qualified: true)} does not implement {TypeNames.Of(openInterface, qualified: true)}.", paramName),
            _ => throw new ArgumentException(
                $"{TypeNames.Of(requestType, qualified: true)} declares more than one {TypeNames.Of(openInterface)} ({Describe(declared)}), "
                + "so its response type cannot be chosen from the object alone. Send it through the typed overload for the response you want.",
                paramName),
        };
    }

    /// <summary>
    /// The response type a request declares that fits a call site expecting <paramref name="expected"/>: the call site
    /// sees the request through the covariant interface, e.g. <c>IRequest&lt;Animal&gt;</c> for a request declared as
    /// <c>IRequest&lt;Dog&gt;</c>.
    /// </summary>
    private static Type CovariantResponseType(Type requestType, Type openInterface, Type expected, string paramName)
    {
        var candidates = new List<Type>();
        foreach (var declared in FindClosedInterfaces(requestType, openInterface))
        {
            var responseType = declared.GenericTypeArguments[0];

            // Variance only ever applies to reference types.
            if (!responseType.IsValueType && expected.IsAssignableFrom(responseType))
            {
                candidates.Add(declared);
            }
        }

        var expectedInterface = openInterface.MakeGenericType(expected);
        return candidates.Count switch
        {
            1 => candidates[0].GenericTypeArguments[0],
            0 => throw new ArgumentException($"{TypeNames.Of(requestType, qualified: true)} does not implement {TypeNames.Of(expectedInterface)}.", paramName),
            _ => throw new ArgumentException(
                $"{TypeNames.Of(requestType, qualified: true)} declares more than one interface that fits {TypeNames.Of(expectedInterface)} ({Describe(candidates)}), "
                + "so the handler to use is ambiguous. Send it as one of those interfaces.",
                paramName),
        };
    }

    private static List<Type> FindClosedInterfaces(Type type, Type openGenericInterface)
    {
        var result = new List<Type>();
        foreach (var candidate in type.GetInterfaces())
        {
            if (candidate.IsGenericType && candidate.GetGenericTypeDefinition() == openGenericInterface)
            {
                result.Add(candidate);
            }
        }

        return result;
    }

    private static string Describe(List<Type> interfaces) => string.Join(", ", interfaces.Select(static i => TypeNames.Of(i)));

    /// <summary>Wrappers for <see cref="Send{TResponse}(IRequest{TResponse}, CancellationToken)"/> call sites that expect <typeparamref name="TResponse"/>.</summary>
    private static class RequestWrappers<TResponse>
    {
        private static readonly TypeKeyedCache<RequestHandlerWrapper<TResponse>> Cache = new();

        public static RequestHandlerWrapper<TResponse> Get(Type requestType) => Cache.GetOrAdd(requestType, static type => Create(type));

        private static RequestHandlerWrapper<TResponse> Create(Type requestType)
        {
            // The request declares this response type itself (a command declares IRequest<Unit> through IRequest).
            if (ImplementsExactly(requestType, typeof(IRequest<TResponse>)))
            {
                return (RequestHandlerWrapper<TResponse>)CreateRequestWrapper(requestType, typeof(TResponse));
            }

            // A covariant call site: the handler for the call-site response type wins when the container has one,
            // otherwise the declared one runs.
            var declaredResponse = CovariantResponseType(requestType, typeof(IRequest<>), typeof(TResponse), "request");
            var declared = CreateRequestWrapper(requestType, declaredResponse);
            var callSite = CreateRequestWrapper(requestType, typeof(TResponse));
            return CreateWrapper<RequestHandlerWrapper<TResponse>>(
                typeof(CovariantRequestHandlerWrapper<,,>).MakeGenericType(requestType, declaredResponse, typeof(TResponse)),
                requestType,
                declared,
                callSite);
        }
    }

    /// <summary>Wrappers for <see cref="CreateStream{TResponse}(IStreamRequest{TResponse}, CancellationToken)"/> call sites that expect <typeparamref name="TResponse"/>.</summary>
    private static class StreamWrappers<TResponse>
    {
        private static readonly TypeKeyedCache<StreamRequestHandlerWrapper<TResponse>> Cache = new();

        public static StreamRequestHandlerWrapper<TResponse> Get(Type requestType) => Cache.GetOrAdd(requestType, static type => Create(type));

        private static StreamRequestHandlerWrapper<TResponse> Create(Type requestType)
        {
            if (ImplementsExactly(requestType, typeof(IStreamRequest<TResponse>)))
            {
                return CreateWrapper<StreamRequestHandlerWrapper<TResponse>>(
                    typeof(StreamRequestHandlerWrapperImpl<,>).MakeGenericType(requestType, typeof(TResponse)),
                    requestType);
            }

            var declaredResponse = CovariantResponseType(requestType, typeof(IStreamRequest<>), typeof(TResponse), "request");
            var declared = CreateWrapper<StreamRequestHandlerBase>(
                typeof(StreamRequestHandlerWrapperImpl<,>).MakeGenericType(requestType, declaredResponse),
                requestType);
            var callSite = CreateWrapper<StreamRequestHandlerBase>(
                typeof(StreamRequestHandlerWrapperImpl<,>).MakeGenericType(requestType, typeof(TResponse)),
                requestType);
            return CreateWrapper<StreamRequestHandlerWrapper<TResponse>>(
                typeof(CovariantStreamRequestHandlerWrapper<,,>).MakeGenericType(requestType, declaredResponse, typeof(TResponse)),
                requestType,
                declared,
                callSite);
        }
    }

    /// <summary><see langword="true"/> when <paramref name="closedInterface"/> is one of the interfaces the type declares, not merely one it converts to through variance.</summary>
    private static bool ImplementsExactly(Type type, Type closedInterface) => Array.IndexOf(type.GetInterfaces(), closedInterface) >= 0;
}
