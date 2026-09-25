// Registration fixtures that make AddCaesar throw by design: handlers nested in generic types that the container
// cannot close, and competing open-generic single handlers. Like MisshapedHandlers.cs they live outside Caesar.Tests,
// so that scanning Caesar.Tests with default options keeps working. Each namespace is one scenario.

namespace Caesar.Tests.Fixtures.Registration.NestedInGeneric
{
    public sealed record Evt : INotification;

    public static class Outer<T>
    {
        /// <summary>Generic only because its declaring type is: it closes the interface over a concrete type.</summary>
        public sealed class Handler : INotificationHandler<Evt>
        {
            public Task Handle(Evt notification, CancellationToken cancellationToken) => Task.CompletedTask;
        }
    }
}

namespace Caesar.Tests.Fixtures.Registration.NestedGenericInGeneric
{
    public static class Outer<T>
    {
        /// <summary>Uses the outer parameter as the response, so the interface's arguments are out of declaration order.</summary>
        public sealed class Handler<TRequest> : IRequestHandler<TRequest, T>
            where TRequest : IRequest<T>
        {
            public Task<T> Handle(TRequest request, CancellationToken cancellationToken) => Task.FromResult(default(T)!);
        }
    }
}

namespace Caesar.Tests.Fixtures.Registration.DuplicateOpen.RequestA
{
    public sealed class FirstRequestHandler<TRequest, TResponse> : IRequestHandler<TRequest, TResponse>
        where TRequest : IRequest<TResponse>
    {
        public Task<TResponse> Handle(TRequest request, CancellationToken cancellationToken) => Task.FromResult(default(TResponse)!);
    }
}

namespace Caesar.Tests.Fixtures.Registration.DuplicateOpen.RequestB
{
    public sealed class SecondRequestHandler<TRequest, TResponse> : IRequestHandler<TRequest, TResponse>
        where TRequest : IRequest<TResponse>
    {
        public Task<TResponse> Handle(TRequest request, CancellationToken cancellationToken) => Task.FromResult(default(TResponse)!);
    }
}

namespace Caesar.Tests.Fixtures.Registration.DuplicateOpen.VoidA
{
    public sealed class FirstVoidHandler<TRequest> : IRequestHandler<TRequest>
        where TRequest : IRequest
    {
        public Task Handle(TRequest request, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}

namespace Caesar.Tests.Fixtures.Registration.DuplicateOpen.VoidB
{
    public sealed class SecondVoidHandler<TRequest> : IRequestHandler<TRequest>
        where TRequest : IRequest
    {
        public Task Handle(TRequest request, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}

namespace Caesar.Tests.Fixtures.Registration.DuplicateOpen.StreamA
{
    public sealed class FirstStreamHandler<TRequest, TResponse> : IStreamRequestHandler<TRequest, TResponse>
        where TRequest : IStreamRequest<TResponse>
    {
        public IAsyncEnumerable<TResponse> Handle(TRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}

namespace Caesar.Tests.Fixtures.Registration.DuplicateOpen.StreamB
{
    public sealed class SecondStreamHandler<TRequest, TResponse> : IStreamRequestHandler<TRequest, TResponse>
        where TRequest : IStreamRequest<TResponse>
    {
        public IAsyncEnumerable<TResponse> Handle(TRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
