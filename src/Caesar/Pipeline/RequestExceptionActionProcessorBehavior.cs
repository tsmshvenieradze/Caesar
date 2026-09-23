using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace Caesar.Pipeline;

/// <summary>
/// Runs every matching <see cref="IRequestExceptionAction{TRequest, TException}"/> when the inner pipeline throws,
/// most specific exception type first, then rethrows the original exception.
/// </summary>
/// <typeparam name="TRequest">The request type.</typeparam>
/// <typeparam name="TResponse">The response type.</typeparam>
public sealed class RequestExceptionActionProcessorBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private static readonly ConcurrentDictionary<Type, (Type ServiceType, MethodInfo Execute)> ActionMetadata = new();

    private readonly IServiceProvider _serviceProvider;

    /// <summary>Creates the behavior.</summary>
    /// <param name="serviceProvider">Provider used to resolve exception actions.</param>
    public RequestExceptionActionProcessorBehavior(IServiceProvider serviceProvider)
        => _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));

    /// <inheritdoc />
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(next);

        try
        {
            return await next(cancellationToken).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // The behavior's job is to route every exception to the registered actions.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            foreach (var exceptionType in ExceptionTypeHierarchy.Of(exception.GetType()))
            {
                var (serviceType, execute) = ActionMetadata.GetOrAdd(exceptionType, static type =>
                {
                    var service = typeof(IRequestExceptionAction<,>).MakeGenericType(typeof(TRequest), type);
                    return (service, service.GetMethod(nameof(IRequestExceptionAction<TRequest, Exception>.Execute))!);
                });

                foreach (var action in _serviceProvider.GetServices(serviceType))
                {
                    if (action is null)
                    {
                        continue;
                    }

                    await ((Task)execute.Invoke(action, [request, exception, cancellationToken])!).ConfigureAwait(false);
                }
            }

            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception).Throw();
            throw; // unreachable; satisfies definite assignment
        }
    }
}
