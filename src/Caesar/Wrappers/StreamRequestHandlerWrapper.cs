using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;

namespace Caesar.Wrappers;

/// <summary>Non-generic entry point for streaming a request whose type is only known at runtime.</summary>
public abstract class StreamRequestHandlerBase
{
    /// <summary>Dispatches <paramref name="request"/> through the stream pipeline to its handler.</summary>
    public abstract IAsyncEnumerable<object?> Handle(object request, IServiceProvider serviceProvider, CancellationToken cancellationToken);

    /// <summary>
    /// Resolves a single stream handler, producing a clear error when none is registered. Any other failure to resolve
    /// it, such as a missing constructor dependency or a throwing constructor, propagates unchanged.
    /// </summary>
    protected static THandler GetHandler<THandler>(IServiceProvider serviceProvider)
        where THandler : notnull
        => serviceProvider.GetService<THandler>() ?? throw RequestHandlerBase.HandlerNotFound(typeof(THandler));
}

/// <summary>Typed entry point for streaming an <see cref="IStreamRequest{TResponse}"/>.</summary>
/// <typeparam name="TResponse">The item type.</typeparam>
public abstract class StreamRequestHandlerWrapper<TResponse> : StreamRequestHandlerBase
{
    /// <summary>Dispatches <paramref name="request"/> through the stream pipeline to its handler.</summary>
    public abstract IAsyncEnumerable<TResponse> Handle(IStreamRequest<TResponse> request, IServiceProvider serviceProvider, CancellationToken cancellationToken);
}

/// <summary>
/// Dispatches <typeparamref name="TRequest"/> to <see cref="IStreamRequestHandler{TRequest, TResponse}"/> through the
/// stream behavior chain. Nothing is resolved or invoked until enumeration starts, and every enumeration builds its
/// own pipeline.
/// </summary>
/// <typeparam name="TRequest">The request type.</typeparam>
/// <typeparam name="TResponse">The item type.</typeparam>
public sealed class StreamRequestHandlerWrapperImpl<TRequest, TResponse> : StreamRequestHandlerWrapper<TResponse>
    where TRequest : IStreamRequest<TResponse>
{
    /// <inheritdoc />
    public override async IAsyncEnumerable<object?> Handle(
        object request,
        IServiceProvider serviceProvider,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var item in BuildPipeline((TRequest)request, serviceProvider, cancellationToken).WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            yield return item;
        }
    }

    /// <inheritdoc />
    public override IAsyncEnumerable<TResponse> Handle(IStreamRequest<TResponse> request, IServiceProvider serviceProvider, CancellationToken cancellationToken)
        => new DeferredStream((TRequest)request, serviceProvider, cancellationToken);

    /// <summary>
    /// Defers the pipeline to enumeration. The token given to <c>CreateStream</c> and the one given to the enumerator
    /// are combined, so cancelling either one stops the stream. Used when both can be cancelled; otherwise
    /// <see cref="DeferredStream"/> forwards to the pipeline without an iterator of its own.
    /// </summary>
    private static async IAsyncEnumerable<TResponse> Deferred(
        TRequest request,
        IServiceProvider serviceProvider,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var item in BuildPipeline(request, serviceProvider, cancellationToken).WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            yield return item;
        }
    }

    private static IAsyncEnumerable<TResponse> BuildPipeline(TRequest request, IServiceProvider serviceProvider, CancellationToken cancellationToken)
    {
        // Null when the mediator is used without AddCaesar: then the handler lookup is always tried.
        var shape = serviceProvider.GetService<StreamRequestShape<TRequest, TResponse>>();

        var behaviors = serviceProvider.GetServices<IStreamPipelineBehavior<TRequest, TResponse>>();
        var list = behaviors as IReadOnlyList<IStreamPipelineBehavior<TRequest, TResponse>> ?? behaviors.ToList();

        return list.Count == 0
            ? ResolveHandler(serviceProvider, shape).Handle(request, cancellationToken)
            : WithBehaviors(request, serviceProvider, shape, list, cancellationToken);
    }

    private static IAsyncEnumerable<TResponse> WithBehaviors(
        TRequest request,
        IServiceProvider serviceProvider,
        StreamRequestShape<TRequest, TResponse>? shape,
        IReadOnlyList<IStreamPipelineBehavior<TRequest, TResponse>> behaviors,
        CancellationToken cancellationToken)
    {
        IAsyncEnumerable<TResponse> Handler() => ResolveHandler(serviceProvider, shape).Handle(request, cancellationToken);

        StreamHandlerDelegate<TResponse> next = Handler;
        for (var i = behaviors.Count - 1; i >= 0; i--)
        {
            var behavior = behaviors[i];
            var inner = next;
            next = () => behavior.Handle(request, inner, cancellationToken);
        }

        return next();
    }

    /// <summary>
    /// The stream <c>CreateStream</c> returns: nothing is resolved or run until enumeration starts, and every enumeration
    /// builds its own pipeline. The pipeline's own enumerator is handed out, so items pass through without another layer.
    /// </summary>
    private sealed class DeferredStream(TRequest request, IServiceProvider serviceProvider, CancellationToken cancellationToken) : IAsyncEnumerable<TResponse>
    {
        public IAsyncEnumerator<TResponse> GetAsyncEnumerator(CancellationToken enumeratorToken = default)
        {
            // Two tokens that can both be cancelled must both stop the stream, which the iterator links.
            if (enumeratorToken.CanBeCanceled && cancellationToken.CanBeCanceled && enumeratorToken != cancellationToken)
            {
                return Deferred(request, serviceProvider, cancellationToken).GetAsyncEnumerator(enumeratorToken);
            }

            var token = enumeratorToken.CanBeCanceled ? enumeratorToken : cancellationToken;
            try
            {
                return BuildPipeline(request, serviceProvider, token).GetAsyncEnumerator(token);
            }
#pragma warning disable CA1031 // Every failure belongs to the caller, on the first MoveNextAsync, as from an async iterator.
            catch (Exception exception)
#pragma warning restore CA1031
            {
                return new FaultedEnumerator(exception);
            }
        }
    }

    /// <summary>An enumerator whose first <c>MoveNextAsync</c> fails with the exception building the pipeline threw.</summary>
    private sealed class FaultedEnumerator(Exception exception) : IAsyncEnumerator<TResponse>
    {
        public TResponse Current => default!;

        public ValueTask<bool> MoveNextAsync() => ValueTask.FromException<bool>(exception);

        public ValueTask DisposeAsync() => default;
    }

    private static IStreamRequestHandler<TRequest, TResponse> ResolveHandler(IServiceProvider serviceProvider, StreamRequestShape<TRequest, TResponse>? shape)
    {
        var handler = shape is null || shape.ProbeHandler
            ? serviceProvider.GetService<IStreamRequestHandler<TRequest, TResponse>>()
            : null;

        return handler ?? throw RequestHandlerBase.HandlerNotFound(typeof(IStreamRequestHandler<TRequest, TResponse>));
    }
}
