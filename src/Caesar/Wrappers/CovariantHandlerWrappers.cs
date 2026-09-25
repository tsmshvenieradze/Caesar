using Microsoft.Extensions.DependencyInjection;

namespace Caesar.Wrappers;

// IRequest<out T> and IStreamRequest<out T> are covariant, so a request declared as IRequest<Dog> can reach Send
// typed as IRequest<Animal>. Such a call goes to the handler registered for the call-site type, IRequest<Animal>, when
// the container has one, as it always did; when it has none, these adapters dispatch it to the wrapper built for the
// declared response type instead, so the handler and behaviors that run are the ones registered for IRequest<Dog>.

/// <summary>Sends a request declared as <c>IRequest&lt;TDeclared&gt;</c> for a call site that expects <typeparamref name="TResponse"/>.</summary>
/// <typeparam name="TRequest">The request type.</typeparam>
/// <typeparam name="TDeclared">The response type the request declares.</typeparam>
/// <typeparam name="TResponse">The response type of the call site, a base type or interface of <typeparamref name="TDeclared"/>.</typeparam>
internal sealed class CovariantRequestHandlerWrapper<TRequest, TDeclared, TResponse> : RequestHandlerWrapper<TResponse>
    where TRequest : IRequest<TDeclared>, IRequest<TResponse>
    where TDeclared : TResponse
{
    private readonly RequestHandlerWrapper<TDeclared> _declared;
    private readonly RequestHandlerWrapper<TResponse> _callSite;

    public CovariantRequestHandlerWrapper(RequestHandlerWrapper<TDeclared> declared, RequestHandlerWrapper<TResponse> callSite)
    {
        _declared = declared ?? throw new ArgumentNullException(nameof(declared));
        _callSite = callSite ?? throw new ArgumentNullException(nameof(callSite));
    }

    /// <inheritdoc />
    public override Task<object?> Handle(object request, IServiceProvider serviceProvider, CancellationToken cancellationToken)
        => HasCallSiteHandler(serviceProvider)
            ? _callSite.Handle(request, serviceProvider, cancellationToken)
            : _declared.Handle(request, serviceProvider, cancellationToken);

    /// <inheritdoc />
    public override Task<TResponse> Handle(IRequest<TResponse> request, IServiceProvider serviceProvider, CancellationToken cancellationToken)
        => HasCallSiteHandler(serviceProvider)
            ? _callSite.Handle(request, serviceProvider, cancellationToken)
            : Upcast(_declared.Handle((IRequest<TDeclared>)request, serviceProvider, cancellationToken));

    private static bool HasCallSiteHandler(IServiceProvider serviceProvider)
        => serviceProvider.GetService<RequestPipelineShape<TRequest, TResponse>>() is { } shape
            ? shape.HasHandler
            : serviceProvider.GetService<IServiceProviderIsService>()?.IsService(typeof(IRequestHandler<TRequest, TResponse>)) == true;

    private static async Task<TResponse> Upcast(Task<TDeclared> response) => await response.ConfigureAwait(false);
}

/// <summary>Streams a request declared as <c>IStreamRequest&lt;TDeclared&gt;</c> for a call site that expects <typeparamref name="TResponse"/>.</summary>
/// <typeparam name="TRequest">The request type.</typeparam>
/// <typeparam name="TDeclared">The item type the request declares.</typeparam>
/// <typeparam name="TResponse">The item type of the call site, a base type or interface of <typeparamref name="TDeclared"/>.</typeparam>
internal sealed class CovariantStreamRequestHandlerWrapper<TRequest, TDeclared, TResponse> : StreamRequestHandlerWrapper<TResponse>
    where TRequest : IStreamRequest<TDeclared>, IStreamRequest<TResponse>
    where TDeclared : class, TResponse
{
    private readonly StreamRequestHandlerWrapper<TDeclared> _declared;
    private readonly StreamRequestHandlerWrapper<TResponse> _callSite;

    public CovariantStreamRequestHandlerWrapper(StreamRequestHandlerWrapper<TDeclared> declared, StreamRequestHandlerWrapper<TResponse> callSite)
    {
        _declared = declared ?? throw new ArgumentNullException(nameof(declared));
        _callSite = callSite ?? throw new ArgumentNullException(nameof(callSite));
    }

    /// <inheritdoc />
    public override IAsyncEnumerable<object?> Handle(object request, IServiceProvider serviceProvider, CancellationToken cancellationToken)
        => HasCallSiteHandler(serviceProvider)
            ? _callSite.Handle(request, serviceProvider, cancellationToken)
            : _declared.Handle(request, serviceProvider, cancellationToken);

    /// <inheritdoc />
    public override IAsyncEnumerable<TResponse> Handle(IStreamRequest<TResponse> request, IServiceProvider serviceProvider, CancellationToken cancellationToken)
        => HasCallSiteHandler(serviceProvider)
            ? _callSite.Handle(request, serviceProvider, cancellationToken)
            : _declared.Handle((IStreamRequest<TDeclared>)request, serviceProvider, cancellationToken);

    private static bool HasCallSiteHandler(IServiceProvider serviceProvider)
        => serviceProvider.GetService<StreamRequestShape<TRequest, TResponse>>() is { } shape
            ? shape.HasHandler
            : serviceProvider.GetService<IServiceProviderIsService>()?.IsService(typeof(IStreamRequestHandler<TRequest, TResponse>)) == true;
}
