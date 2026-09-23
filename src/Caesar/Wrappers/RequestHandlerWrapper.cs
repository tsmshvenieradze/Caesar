using Microsoft.Extensions.DependencyInjection;

namespace Caesar.Wrappers;

/// <summary>Non-generic entry point for dispatching a request whose type is only known at runtime.</summary>
public abstract class RequestHandlerBase
{
    /// <summary>Dispatches <paramref name="request"/> through the pipeline to its handler.</summary>
    public abstract Task<object?> Handle(object request, IServiceProvider serviceProvider, CancellationToken cancellationToken);

    /// <summary>Resolves a single handler, producing a clear error when none is registered.</summary>
    protected static THandler GetHandler<THandler>(IServiceProvider serviceProvider)
        where THandler : notnull
    {
        THandler handler;
        try
        {
            handler = serviceProvider.GetRequiredService<THandler>();
        }
        catch (InvalidOperationException e)
        {
            var handlerType = typeof(THandler);
            var requestType = handlerType.IsGenericType ? handlerType.GenericTypeArguments[0] : handlerType;
            throw new InvalidOperationException(
                $"No handler was found for request of type {requestType.FullName}. Expected an implementation of {handlerType.Name.Split('`')[0]}<{string.Join(", ", handlerType.GenericTypeArguments.Select(static t => t.Name))}>. Register your handlers with the container (services.AddCaesar(cfg => cfg.RegisterServicesFromAssemblyContaining<...>())).",
                e);
        }

        return handler;
    }
}

/// <summary>Typed entry point for dispatching an <see cref="IRequest{TResponse}"/>.</summary>
/// <typeparam name="TResponse">The response type.</typeparam>
public abstract class RequestHandlerWrapper<TResponse> : RequestHandlerBase
{
    /// <summary>Dispatches <paramref name="request"/> through the pipeline to its handler.</summary>
    public abstract Task<TResponse> Handle(IRequest<TResponse> request, IServiceProvider serviceProvider, CancellationToken cancellationToken);
}

/// <summary>Dispatches <typeparamref name="TRequest"/> to <see cref="IRequestHandler{TRequest, TResponse}"/> through the behavior chain.</summary>
/// <typeparam name="TRequest">The request type.</typeparam>
/// <typeparam name="TResponse">The response type.</typeparam>
public sealed class RequestHandlerWrapperImpl<TRequest, TResponse> : RequestHandlerWrapper<TResponse>
    where TRequest : IRequest<TResponse>
{
    /// <inheritdoc />
    public override async Task<object?> Handle(object request, IServiceProvider serviceProvider, CancellationToken cancellationToken)
        => await Handle((IRequest<TResponse>)request, serviceProvider, cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public override Task<TResponse> Handle(IRequest<TResponse> request, IServiceProvider serviceProvider, CancellationToken cancellationToken)
    {
        var typedRequest = (TRequest)request;

        Task<TResponse> Handler(CancellationToken t = default)
            => GetHandler<IRequestHandler<TRequest, TResponse>>(serviceProvider)
                .Handle(typedRequest, t == default ? cancellationToken : t);

        return PipelineBuilder.Build<TRequest, TResponse>(serviceProvider, typedRequest, Handler)(cancellationToken);
    }
}

/// <summary>
/// Dispatches a command (<see cref="IRequest"/>) to <see cref="IRequestHandler{TRequest}"/>, falling back to
/// <see cref="IRequestHandler{TRequest, TResponse}"/> with a <see cref="Unit"/> response when that is what was registered.
/// </summary>
/// <typeparam name="TRequest">The request type.</typeparam>
public sealed class RequestHandlerWrapperImpl<TRequest> : RequestHandlerWrapper<Unit>
    where TRequest : IRequest
{
    /// <inheritdoc />
    public override async Task<object?> Handle(object request, IServiceProvider serviceProvider, CancellationToken cancellationToken)
        => await Handle((IRequest<Unit>)request, serviceProvider, cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public override Task<Unit> Handle(IRequest<Unit> request, IServiceProvider serviceProvider, CancellationToken cancellationToken)
    {
        var typedRequest = (TRequest)request;

        async Task<Unit> Handler(CancellationToken t = default)
        {
            var token = t == default ? cancellationToken : t;

            var voidHandler = serviceProvider.GetService<IRequestHandler<TRequest>>();
            if (voidHandler is not null)
            {
                await voidHandler.Handle(typedRequest, token).ConfigureAwait(false);
                return Unit.Value;
            }

            var unitHandler = serviceProvider.GetService<IRequestHandler<TRequest, Unit>>();
            if (unitHandler is not null)
            {
                return await unitHandler.Handle(typedRequest, token).ConfigureAwait(false);
            }

            // Neither variant is registered; resolving the primary one produces the standard error.
            await GetHandler<IRequestHandler<TRequest>>(serviceProvider).Handle(typedRequest, token).ConfigureAwait(false);
            return Unit.Value;
        }

        return PipelineBuilder.Build<TRequest, Unit>(serviceProvider, typedRequest, Handler)(cancellationToken);
    }
}

internal static class PipelineBuilder
{
    /// <summary>
    /// Composes the registered <see cref="IPipelineBehavior{TRequest, TResponse}"/> instances around <paramref name="handler"/>.
    /// The first registered behavior becomes the outermost. A behavior that calls <c>next()</c> without a token keeps the
    /// token that is currently flowing through the chain; passing a token replaces it for the remainder of the chain.
    /// </summary>
    public static RequestHandlerDelegate<TResponse> Build<TRequest, TResponse>(
        IServiceProvider serviceProvider,
        TRequest request,
        RequestHandlerDelegate<TResponse> handler)
        where TRequest : notnull
    {
        var behaviors = serviceProvider.GetServices<IPipelineBehavior<TRequest, TResponse>>();

        // Materialise once so a lazily-evaluated container enumeration is not re-run per behavior.
        var list = behaviors as IReadOnlyList<IPipelineBehavior<TRequest, TResponse>> ?? behaviors.ToList();

        var next = handler;
        for (var i = list.Count - 1; i >= 0; i--)
        {
            var behavior = list[i];
            var inner = next;
            next = token => behavior.Handle(
                request,
                innerToken => inner(innerToken == default ? token : innerToken),
                token);
        }

        return next;
    }
}
