using System.Reflection;
using System.Runtime.ExceptionServices;

namespace Caesar.Pipeline;

/// <summary>
/// Catches exceptions from the inner pipeline and dispatches them to the matching
/// <see cref="IRequestExceptionHandler{TRequest, TResponse, TException}"/> instances, most specific exception type first.
/// If a handler marks the exception as handled, its response is returned; otherwise the exception is rethrown
/// with its original stack trace.
/// </summary>
/// <remarks>
/// <para>
/// A handler class that implements the interface for several types in the exception's hierarchy runs once, for the
/// most specific of them; several registrations of one class for the same type all run. With
/// <see cref="DependencyInjection.CaesarServiceConfiguration.BypassExceptionHandlingOnCallerCancellation"/> set, an
/// <see cref="OperationCanceledException"/> thrown while the caller's cancellation token is cancelled is rethrown
/// without reaching any handler.
/// </para>
/// <para>
/// This stage is not registered as an <see cref="IPipelineBehavior{TRequest, TResponse}"/>. In a container set up with
/// <c>AddCaesar</c>, the mediator composes it into the pipeline of each request type that has an exception handler,
/// outside the processors and every pipeline behavior: a closed handler for that request and response, whatever its
/// exception type, or an open-generic one, which applies to every request.
/// <see cref="DependencyInjection.RequestExceptionActionProcessorStrategy"/> sets whether it sits inside or outside
/// <see cref="RequestExceptionActionProcessorBehavior{TRequest, TResponse}"/>. Neither the number nor the order of
/// <c>AddCaesar</c> calls changes that, and handlers registered after <c>AddCaesar</c> count too. If this behavior is
/// registered as a pipeline behavior anyway, the mediator leaves its own stage out, so it runs once, at that
/// registration's position.
/// </para>
/// </remarks>
/// <typeparam name="TRequest">The request type.</typeparam>
/// <typeparam name="TResponse">The response type.</typeparam>
public sealed class RequestExceptionProcessorBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private static readonly MethodInfo InvokeHandlerMethod =
        typeof(RequestExceptionProcessorBehavior<TRequest, TResponse>).GetMethod(nameof(InvokeHandler), BindingFlags.NonPublic | BindingFlags.Static)!;

    // Keyed by exception type. An exception type from a collectible assembly does not keep that assembly loaded.
    private static readonly TypeKeyedCache<HandlerLevel> HandlerMetadata = new();

    private readonly IServiceProvider _serviceProvider;
    private readonly bool _bypassCallerCancellation;

    /// <summary>Creates the behavior.</summary>
    /// <param name="serviceProvider">Provider used to resolve exception handlers.</param>
    public RequestExceptionProcessorBehavior(IServiceProvider serviceProvider)
        : this(serviceProvider, bypassCallerCancellation: false)
    {
    }

    /// <summary>Creates the stage the mediator composes, honouring the container's cancellation option.</summary>
    internal RequestExceptionProcessorBehavior(IServiceProvider serviceProvider, bool bypassCallerCancellation)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _bypassCallerCancellation = bypassCallerCancellation;
    }

    private delegate Task HandlerInvoker(object handler, TRequest request, Exception exception, RequestExceptionHandlerState<TResponse> state, CancellationToken cancellationToken);

    /// <inheritdoc />
    public Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(next);

        Task<TResponse> pending;
        try
        {
            pending = next(cancellationToken);
        }
#pragma warning disable CA1031 // A synchronous throw is routed like a faulted task, and still surfaces as one.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            return RecoverOrRethrowAsync(request, exception, cancellationToken);
        }

        // Success-path fast path: no state machine when the inner pipeline already completed successfully.
        return pending is { IsCompletedSuccessfully: true } ? pending : AwaitAsync(request, pending, cancellationToken);
    }

    private async Task<TResponse> AwaitAsync(TRequest request, Task<TResponse> pending, CancellationToken cancellationToken)
    {
        try
        {
            return await pending.ConfigureAwait(false);
        }
#pragma warning disable CA1031 // The behavior's job is to route every exception to the registered handlers.
        catch (Exception exception) when (!IsBypassed(exception, cancellationToken))
#pragma warning restore CA1031
        {
            var state = await RunHandlersAsync(request, exception, cancellationToken).ConfigureAwait(false);
            if (state.Handled)
            {
                return state.Response!;
            }

            ExceptionDispatchInfo.Capture(exception).Throw();
            throw; // unreachable; satisfies definite assignment
        }
    }

    private async Task<TResponse> RecoverOrRethrowAsync(TRequest request, Exception exception, CancellationToken cancellationToken)
    {
        if (!IsBypassed(exception, cancellationToken))
        {
            var state = await RunHandlersAsync(request, exception, cancellationToken).ConfigureAwait(false);
            if (state.Handled)
            {
                return state.Response!;
            }
        }

        ExceptionDispatchInfo.Capture(exception).Throw();
        return default!; // unreachable
    }

    private bool IsBypassed(Exception exception, CancellationToken cancellationToken)
        => _bypassCallerCancellation && ExceptionFilters.IsCallerCancellation(exception, cancellationToken);

    private async Task<RequestExceptionHandlerState<TResponse>> RunHandlersAsync(TRequest request, Exception exception, CancellationToken cancellationToken)
    {
        var state = new RequestExceptionHandlerState<TResponse>();

        // Classes that ran for a more specific exception type. Within one level every registration runs.
        HashSet<Type>? ranBefore = null;
        List<Type>? ranHere = null;

        foreach (var exceptionType in ExceptionTypeHierarchy.Of(exception.GetType()))
        {
            var level = HandlerMetadata.GetOrAdd(exceptionType, static type =>
            {
                var service = typeof(IRequestExceptionHandler<,,>).MakeGenericType(typeof(TRequest), typeof(TResponse), type);
                return new HandlerLevel(typeof(IEnumerable<>).MakeGenericType(service), InvokeHandlerMethod.MakeGenericMethod(type).CreateDelegate<HandlerInvoker>());
            });

            if (_serviceProvider.GetService(level.EnumerableType) is not IEnumerable<object?> handlers)
            {
                continue;
            }

            ranHere?.Clear();
            foreach (var handler in handlers)
            {
                // Like MediatR, a class registered for several levels of the hierarchy runs once, most specific first.
                if (handler is null || ranBefore?.Contains(handler.GetType()) == true)
                {
                    continue;
                }

                (ranHere ??= []).Add(handler.GetType());

                var task = level.Invoke(handler, request, exception, state, cancellationToken)
                    ?? throw new InvalidOperationException(
                        $"The exception handler '{handler.GetType().FullName}' returned a null Task from Handle.", exception);

                await task.ConfigureAwait(false);

                if (state.Handled)
                {
                    return state;
                }
            }

            if (ranHere is { Count: > 0 })
            {
                (ranBefore ??= []).UnionWith(ranHere);
            }
        }

        return state;
    }

    /// <summary>Strongly typed call to one closing of the handler interface, so a synchronous throw is never wrapped.</summary>
    private static Task InvokeHandler<TException>(object handler, TRequest request, Exception exception, RequestExceptionHandlerState<TResponse> state, CancellationToken cancellationToken)
        where TException : Exception
        => ((IRequestExceptionHandler<TRequest, TResponse, TException>)handler).Handle(request, (TException)exception, state, cancellationToken);

    /// <summary>What one level of the exception's hierarchy needs: the handler collection to resolve and a typed call into its handlers.</summary>
    private sealed class HandlerLevel(Type enumerableType, HandlerInvoker invoke)
    {
        public Type EnumerableType { get; } = enumerableType;

        public HandlerInvoker Invoke { get; } = invoke;
    }
}
