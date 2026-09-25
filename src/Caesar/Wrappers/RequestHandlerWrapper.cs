using Microsoft.Extensions.DependencyInjection;

namespace Caesar.Wrappers;

/// <summary>Non-generic entry point for dispatching a request whose type is only known at runtime.</summary>
public abstract class RequestHandlerBase
{
    /// <summary>Dispatches <paramref name="request"/> through the pipeline to its handler.</summary>
    public abstract Task<object?> Handle(object request, IServiceProvider serviceProvider, CancellationToken cancellationToken);

    /// <summary>
    /// Resolves a single handler, producing a clear error when none is registered. Any other failure to resolve it,
    /// such as a missing constructor dependency or a throwing constructor, propagates unchanged.
    /// </summary>
    protected static THandler GetHandler<THandler>(IServiceProvider serviceProvider)
        where THandler : notnull
        => serviceProvider.GetService<THandler>() ?? throw HandlerNotFound(typeof(THandler));

    /// <summary>The error reported when no handler of type <paramref name="handlerType"/> is registered.</summary>
    internal static InvalidOperationException HandlerNotFound(Type handlerType)
    {
        var requestType = handlerType.IsGenericType ? handlerType.GenericTypeArguments[0] : handlerType;
        return new InvalidOperationException(
            $"No handler was found for request of type {TypeNames.Of(requestType, qualified: true)}. Expected an implementation of {TypeNames.Of(handlerType)}. "
            + "Register your handlers with the container (services.AddCaesar(cfg => cfg.RegisterServicesFromAssemblyContaining<...>())).");
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

        // Null when the mediator is used without AddCaesar: no built-in stages then, and every handler lookup is tried.
        var shape = serviceProvider.GetService<RequestPipelineShape<TRequest, TResponse>>();
        if (shape is { HasStages: true })
        {
            return WithBehaviors(typedRequest, serviceProvider, shape, behaviors: null, cancellationToken);
        }

        var behaviors = PipelineBuilder.GetBehaviors<TRequest, TResponse>(serviceProvider);
        return behaviors.Count == 0
            ? ResolveHandler(serviceProvider, shape).Handle(typedRequest, cancellationToken)
            : WithBehaviors(typedRequest, serviceProvider, shape, behaviors, cancellationToken);
    }

    // Separate from Handle because the compiler allocates the closure for captured parameters when the method that
    // declares the lambda starts, which would put that allocation on the fast path above.
    private static Task<TResponse> WithBehaviors(
        TRequest request,
        IServiceProvider serviceProvider,
        RequestPipelineShape<TRequest, TResponse>? shape,
        IReadOnlyList<IPipelineBehavior<TRequest, TResponse>>? behaviors,
        CancellationToken cancellationToken)
    {
        Task<TResponse> Handler(CancellationToken t = default)
            => ResolveHandler(serviceProvider, shape).Handle(request, t == default ? cancellationToken : t);

        return PipelineBuilder.Build(serviceProvider, request, shape, behaviors, Handler)(cancellationToken);
    }

    private static IRequestHandler<TRequest, TResponse> ResolveHandler(IServiceProvider serviceProvider, RequestPipelineShape<TRequest, TResponse>? shape)
    {
        var handler = shape is null || shape.ProbeHandler
            ? serviceProvider.GetService<IRequestHandler<TRequest, TResponse>>()
            : null;

        return handler ?? throw HandlerNotFound(typeof(IRequestHandler<TRequest, TResponse>));
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

        var shape = serviceProvider.GetService<RequestPipelineShape<TRequest, Unit>>();
        if (shape is { HasStages: true })
        {
            return WithBehaviors(typedRequest, serviceProvider, shape, behaviors: null, cancellationToken);
        }

        var behaviors = PipelineBuilder.GetBehaviors<TRequest, Unit>(serviceProvider);
        return behaviors.Count == 0
            ? HandleCore(typedRequest, serviceProvider, shape, cancellationToken)
            : WithBehaviors(typedRequest, serviceProvider, shape, behaviors, cancellationToken);
    }

    private static Task<Unit> WithBehaviors(
        TRequest request,
        IServiceProvider serviceProvider,
        RequestPipelineShape<TRequest, Unit>? shape,
        IReadOnlyList<IPipelineBehavior<TRequest, Unit>>? behaviors,
        CancellationToken cancellationToken)
    {
        Task<Unit> Handler(CancellationToken t = default)
            => HandleCore(request, serviceProvider, shape, t == default ? cancellationToken : t);

        return PipelineBuilder.Build(serviceProvider, request, shape, behaviors, Handler)(cancellationToken);
    }

    private static async Task<Unit> HandleCore(
        TRequest request,
        IServiceProvider serviceProvider,
        RequestPipelineShape<TRequest, Unit>? shape,
        CancellationToken cancellationToken)
    {
        if (shape is null || shape.ProbeVoidHandler)
        {
            var voidHandler = serviceProvider.GetService<IRequestHandler<TRequest>>();
            if (voidHandler is not null)
            {
                await voidHandler.Handle(request, cancellationToken).ConfigureAwait(false);
                return Unit.Value;
            }
        }

        if (shape is null || shape.ProbeHandler)
        {
            var unitHandler = serviceProvider.GetService<IRequestHandler<TRequest, Unit>>();
            if (unitHandler is not null)
            {
                return await unitHandler.Handle(request, cancellationToken).ConfigureAwait(false);
            }
        }

        throw HandlerNotFound(typeof(IRequestHandler<TRequest>));
    }
}

internal static class PipelineBuilder
{
    /// <summary>The user's <see cref="IPipelineBehavior{TRequest, TResponse}"/> registrations, in container order.</summary>
    public static IReadOnlyList<IPipelineBehavior<TRequest, TResponse>> GetBehaviors<TRequest, TResponse>(IServiceProvider serviceProvider)
        where TRequest : notnull
    {
        var behaviors = serviceProvider.GetServices<IPipelineBehavior<TRequest, TResponse>>();

        // Materialise once so a lazily-evaluated container enumeration is not re-run per behavior.
        return behaviors as IReadOnlyList<IPipelineBehavior<TRequest, TResponse>> ?? behaviors.ToList();
    }

    /// <summary>
    /// Composes the pipeline around <paramref name="handler"/>, outermost first: the built-in stages the
    /// <paramref name="shape"/> lists, then the registered <see cref="IPipelineBehavior{TRequest, TResponse}"/> instances
    /// in container order (resolved here when <paramref name="behaviors"/> is <see langword="null"/>). A behavior that
    /// calls <c>next()</c> without a token keeps the token that is currently flowing through the chain; passing a token
    /// replaces it for the remainder of the chain.
    /// </summary>
    public static RequestHandlerDelegate<TResponse> Build<TRequest, TResponse>(
        IServiceProvider serviceProvider,
        TRequest request,
        RequestPipelineShape<TRequest, TResponse>? shape,
        IReadOnlyList<IPipelineBehavior<TRequest, TResponse>>? behaviors,
        RequestHandlerDelegate<TResponse> handler)
        where TRequest : IRequest<TResponse>
    {
        behaviors ??= GetBehaviors<TRequest, TResponse>(serviceProvider);

        // Behaviors registered before the first AddCaesar call run outside the built-in stages, the rest inside them.
        var outer = Math.Min(shape?.OuterBehaviorCount ?? 0, behaviors.Count);

        // The chain is built from the inside out.
        var next = handler;
        for (var i = behaviors.Count - 1; i >= outer; i--)
        {
            next = Wrap(behaviors[i], request, next);
        }

        for (var i = (shape?.StageCount ?? 0) - 1; i >= 0; i--)
        {
            next = Wrap(shape!.CreateStage(i, serviceProvider), request, next);
        }

        for (var i = outer - 1; i >= 0; i--)
        {
            next = Wrap(behaviors[i], request, next);
        }

        return next;
    }

    private static RequestHandlerDelegate<TResponse> Wrap<TRequest, TResponse>(
        IPipelineBehavior<TRequest, TResponse> behavior,
        TRequest request,
        RequestHandlerDelegate<TResponse> inner)
        where TRequest : notnull
        => token => behavior.Handle(
            request,
            innerToken => inner(innerToken == default ? token : innerToken),
            token);
}
