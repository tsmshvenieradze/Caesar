using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;

namespace Caesar.Wrappers;

/// <summary>Non-generic entry point for streaming a request whose type is only known at runtime.</summary>
public abstract class StreamRequestHandlerBase
{
    /// <summary>Dispatches <paramref name="request"/> through the stream pipeline to its handler.</summary>
    public abstract IAsyncEnumerable<object?> Handle(object request, IServiceProvider serviceProvider, CancellationToken cancellationToken);

    /// <summary>Resolves a single stream handler, producing a clear error when none is registered.</summary>
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

/// <summary>Typed entry point for streaming an <see cref="IStreamRequest{TResponse}"/>.</summary>
/// <typeparam name="TResponse">The item type.</typeparam>
public abstract class StreamRequestHandlerWrapper<TResponse> : StreamRequestHandlerBase
{
    /// <summary>Dispatches <paramref name="request"/> through the stream pipeline to its handler.</summary>
    public abstract IAsyncEnumerable<TResponse> Handle(IStreamRequest<TResponse> request, IServiceProvider serviceProvider, CancellationToken cancellationToken);
}

/// <summary>Dispatches <typeparamref name="TRequest"/> to <see cref="IStreamRequestHandler{TRequest, TResponse}"/> through the stream behavior chain.</summary>
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
        await foreach (var item in Handle((IStreamRequest<TResponse>)request, serviceProvider, cancellationToken).WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            yield return item;
        }
    }

    /// <inheritdoc />
    public override IAsyncEnumerable<TResponse> Handle(IStreamRequest<TResponse> request, IServiceProvider serviceProvider, CancellationToken cancellationToken)
    {
        var typedRequest = (TRequest)request;

        IAsyncEnumerable<TResponse> Handler()
            => GetHandler<IStreamRequestHandler<TRequest, TResponse>>(serviceProvider).Handle(typedRequest, cancellationToken);

        var behaviors = serviceProvider.GetServices<IStreamPipelineBehavior<TRequest, TResponse>>();
        var list = behaviors as IReadOnlyList<IStreamPipelineBehavior<TRequest, TResponse>> ?? behaviors.ToList();

        StreamHandlerDelegate<TResponse> next = Handler;
        for (var i = list.Count - 1; i >= 0; i--)
        {
            var behavior = list[i];
            var inner = next;
            next = () => behavior.Handle(typedRequest, inner, cancellationToken);
        }

        return next();
    }
}
