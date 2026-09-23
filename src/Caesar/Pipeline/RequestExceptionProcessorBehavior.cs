using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace Caesar.Pipeline;

/// <summary>
/// Catches exceptions from the inner pipeline and dispatches them to the matching
/// <see cref="IRequestExceptionHandler{TRequest, TResponse, TException}"/> instances, most specific exception type first.
/// If a handler marks the exception as handled, its response is returned; otherwise the exception is rethrown
/// with its original stack trace.
/// </summary>
/// <typeparam name="TRequest">The request type.</typeparam>
/// <typeparam name="TResponse">The response type.</typeparam>
public sealed class RequestExceptionProcessorBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private static readonly ConcurrentDictionary<Type, (Type ServiceType, MethodInfo Handle)> HandlerMetadata = new();

    private readonly IServiceProvider _serviceProvider;

    /// <summary>Creates the behavior.</summary>
    /// <param name="serviceProvider">Provider used to resolve exception handlers.</param>
    public RequestExceptionProcessorBehavior(IServiceProvider serviceProvider)
        => _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));

    /// <inheritdoc />
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(next);

        try
        {
            return await next(cancellationToken).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // The behavior's job is to route every exception to the registered handlers.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            var state = new RequestExceptionHandlerState<TResponse>();

            foreach (var exceptionType in ExceptionTypeHierarchy.Of(exception.GetType()))
            {
                var (serviceType, handle) = HandlerMetadata.GetOrAdd(exceptionType, static type =>
                {
                    var service = typeof(IRequestExceptionHandler<,,>).MakeGenericType(typeof(TRequest), typeof(TResponse), type);
                    return (service, service.GetMethod(nameof(IRequestExceptionHandler<TRequest, TResponse, Exception>.Handle))!);
                });

                foreach (var handler in _serviceProvider.GetServices(serviceType))
                {
                    if (handler is null)
                    {
                        continue;
                    }

                    await ((Task)handle.Invoke(handler, [request, exception, state, cancellationToken])!).ConfigureAwait(false);

                    if (state.Handled)
                    {
                        return state.Response!;
                    }
                }
            }

            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception).Throw();
            throw; // unreachable; satisfies definite assignment
        }
    }
}
