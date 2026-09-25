using System.Collections;
using System.Reflection;
using System.Runtime.ExceptionServices;

namespace Caesar.Pipeline;

/// <summary>
/// Runs every matching <see cref="IRequestExceptionAction{TRequest, TException}"/> when the inner pipeline throws,
/// most specific exception type first, then rethrows the original exception.
/// </summary>
/// <remarks>
/// <para>
/// An action that fails, synchronously or asynchronously, does not stop the remaining actions and does not replace
/// the original exception, which is always rethrown with its original stack trace. The failures are attached to the
/// original exception's <see cref="Exception.Data"/> under the key <c>"Caesar.ExceptionActionFailures"</c> as an
/// <see cref="IReadOnlyList{T}"/> of <see cref="Exception"/>, in the order the actions ran; failures from a nested
/// <c>Send</c> that already attached the key come first. The list keeps the 16 most recent failures, so an exception
/// instance that several sends share (a cached failure) does not collect them without bound.
/// </para>
/// <para>
/// An action class that implements the interface for several types in the exception's hierarchy runs once, for the
/// most specific of them; several registrations of one class for the same type all run. With
/// <see cref="DependencyInjection.CaesarServiceConfiguration.BypassExceptionHandlingOnCallerCancellation"/> set, an
/// <see cref="OperationCanceledException"/> thrown while the caller's cancellation token is cancelled is rethrown
/// without running any action.
/// </para>
/// <para>
/// This stage is not registered as an <see cref="IPipelineBehavior{TRequest, TResponse}"/>. In a container set up with
/// <c>AddCaesar</c>, the mediator composes it into the pipeline of each request type that has an exception action,
/// outside the processors and every pipeline behavior: a closed action for that request, whatever its exception type,
/// or an open-generic one, which applies to every request.
/// <see cref="DependencyInjection.RequestExceptionActionProcessorStrategy"/> sets whether it sits outside or inside
/// <see cref="RequestExceptionProcessorBehavior{TRequest, TResponse}"/>. Neither the number nor the order of
/// <c>AddCaesar</c> calls changes that, and actions registered after <c>AddCaesar</c> count too. If this behavior is
/// registered as a pipeline behavior anyway, the mediator leaves its own stage out, so it runs once, at that
/// registration's position.
/// </para>
/// </remarks>
/// <typeparam name="TRequest">The request type.</typeparam>
/// <typeparam name="TResponse">The response type.</typeparam>
public sealed class RequestExceptionActionProcessorBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private const string FailuresKey = "Caesar.ExceptionActionFailures";

    /// <summary>The most failures kept on one exception instance.</summary>
    private const int MaxRecordedFailures = 16;

    private static readonly MethodInfo InvokeActionMethod =
        typeof(RequestExceptionActionProcessorBehavior<TRequest, TResponse>).GetMethod(nameof(InvokeAction), BindingFlags.NonPublic | BindingFlags.Static)!;

    // Keyed by exception type. An exception type from a collectible assembly does not keep that assembly loaded.
    private static readonly TypeKeyedCache<ActionLevel> ActionMetadata = new();

    private readonly IServiceProvider _serviceProvider;
    private readonly bool _bypassCallerCancellation;

    /// <summary>Creates the behavior.</summary>
    /// <param name="serviceProvider">Provider used to resolve exception actions.</param>
    public RequestExceptionActionProcessorBehavior(IServiceProvider serviceProvider)
        : this(serviceProvider, bypassCallerCancellation: false)
    {
    }

    /// <summary>Creates the stage the mediator composes, honouring the container's cancellation option.</summary>
    internal RequestExceptionActionProcessorBehavior(IServiceProvider serviceProvider, bool bypassCallerCancellation)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _bypassCallerCancellation = bypassCallerCancellation;
    }

    private delegate Task ActionInvoker(object action, TRequest request, Exception exception, CancellationToken cancellationToken);

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
            return RunActionsAndRethrowAsync(request, exception, cancellationToken);
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
#pragma warning disable CA1031 // The behavior's job is to route every exception to the registered actions.
        catch (Exception exception) when (!IsBypassed(exception, cancellationToken))
#pragma warning restore CA1031
        {
            await RunActionsAsync(request, exception, cancellationToken).ConfigureAwait(false);

            ExceptionDispatchInfo.Capture(exception).Throw();
            throw; // unreachable; satisfies definite assignment
        }
    }

    private async Task<TResponse> RunActionsAndRethrowAsync(TRequest request, Exception exception, CancellationToken cancellationToken)
    {
        if (!IsBypassed(exception, cancellationToken))
        {
            await RunActionsAsync(request, exception, cancellationToken).ConfigureAwait(false);
        }

        ExceptionDispatchInfo.Capture(exception).Throw();
        return default!; // unreachable
    }

    private bool IsBypassed(Exception exception, CancellationToken cancellationToken)
        => _bypassCallerCancellation && ExceptionFilters.IsCallerCancellation(exception, cancellationToken);

    /// <summary>Runs every matching action and records their failures on <paramref name="exception"/>; never throws.</summary>
    private async Task RunActionsAsync(TRequest request, Exception exception, CancellationToken cancellationToken)
    {
        // Classes that ran for a more specific exception type. Within one level every registration runs.
        HashSet<Type>? ranBefore = null;
        List<Type>? ranHere = null;
        List<Exception>? failures = null;

        foreach (var exceptionType in ExceptionTypeHierarchy.Of(exception.GetType()))
        {
            var level = ActionMetadata.GetOrAdd(exceptionType, static type =>
            {
                var service = typeof(IRequestExceptionAction<,>).MakeGenericType(typeof(TRequest), type);
                return new ActionLevel(typeof(IEnumerable<>).MakeGenericType(service), InvokeActionMethod.MakeGenericMethod(type).CreateDelegate<ActionInvoker>());
            });

            if (_serviceProvider.GetService(level.EnumerableType) is not IEnumerable<object?> actions)
            {
                continue;
            }

            ranHere?.Clear();
            foreach (var action in actions)
            {
                // Like MediatR, a class registered for several levels of the hierarchy runs once, most specific first.
                if (action is null || ranBefore?.Contains(action.GetType()) == true)
                {
                    continue;
                }

                (ranHere ??= []).Add(action.GetType());

                try
                {
                    var task = level.Invoke(action, request, exception, cancellationToken)
                        ?? throw new InvalidOperationException(
                            $"The exception action '{action.GetType().FullName}' returned a null Task from Execute.");

                    await task.ConfigureAwait(false);
                }
#pragma warning disable CA1031 // A failing action must neither stop the others nor replace the original exception.
                catch (Exception failure)
#pragma warning restore CA1031
                {
                    // An action that rethrows the original is not a separate failure; the original is rethrown anyway.
                    if (!ReferenceEquals(failure, exception))
                    {
                        (failures ??= []).Add(failure);
                    }
                }
            }

            if (ranHere is { Count: > 0 })
            {
                (ranBefore ??= []).UnionWith(ranHere);
            }
        }

        if (failures is not null)
        {
            AttachFailures(exception, failures);
        }
    }

    /// <summary>
    /// Records action failures on the original exception, after any recorded by a nested <c>Send</c>. Only the most
    /// recent ones are kept: an exception instance can be thrown again by later sends (a cached failure), and must not
    /// collect failures without bound.
    /// </summary>
    private static void AttachFailures(Exception exception, List<Exception> failures)
    {
        IDictionary data = exception.Data;
        if (data.IsReadOnly)
        {
            return;
        }

        // Exception.Data is not thread-safe, and a shared exception can fail several sends at once.
        lock (data)
        {
            if (data[FailuresKey] is IEnumerable<Exception> earlier)
            {
                failures.InsertRange(0, earlier);
            }

            if (failures.Count > MaxRecordedFailures)
            {
                failures.RemoveRange(0, failures.Count - MaxRecordedFailures);
            }

            data[FailuresKey] = failures.AsReadOnly();
        }
    }

    /// <summary>Strongly typed call to one closing of the action interface, so a synchronous throw is never wrapped.</summary>
    private static Task InvokeAction<TException>(object action, TRequest request, Exception exception, CancellationToken cancellationToken)
        where TException : Exception
        => ((IRequestExceptionAction<TRequest, TException>)action).Execute(request, (TException)exception, cancellationToken);

    /// <summary>What one level of the exception's hierarchy needs: the action collection to resolve and a typed call into its actions.</summary>
    private sealed class ActionLevel(Type enumerableType, ActionInvoker invoke)
    {
        public Type EnumerableType { get; } = enumerableType;

        public ActionInvoker Invoke { get; } = invoke;
    }
}
