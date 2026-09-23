// Handlers deliberately shaped so Microsoft.Extensions.DependencyInjection cannot close them.
// They live in their own assembly because AddCaesar now refuses to scan an assembly containing one,
// which would otherwise take down every test that scans Caesar.Tests with default options.

namespace Caesar.Tests.Fixtures.ArityMismatch
{
    public sealed record Query<T>(T Value) : IRequest<T>;

    /// <summary>Declares one type parameter but closes <see cref="IRequestHandler{TRequest, TResponse}"/> over two.</summary>
    public sealed class ArityMismatchHandler<T> : IRequestHandler<Query<T>, T>
    {
        public Task<T> Handle(Query<T> request, CancellationToken cancellationToken) => Task.FromResult(request.Value);
    }
}

namespace Caesar.Tests.Fixtures.ReversedParameters
{
    /// <summary>Right arity, wrong order: the container maps type parameters positionally.</summary>
    public sealed class ReversedParametersHandler<TResponse, TRequest> : IRequestHandler<TRequest, TResponse>
        where TRequest : IRequest<TResponse>
    {
        public Task<TResponse> Handle(TRequest request, CancellationToken cancellationToken) => Task.FromResult(default(TResponse)!);
    }
}

namespace Caesar.Tests.Fixtures.WrappedNotification
{
    public sealed record Envelope<T>(T Value) : INotification;

    /// <summary>A notification handler closed over a constructed type rather than over its own parameter.</summary>
    public sealed class EnvelopeHandler<T> : INotificationHandler<Envelope<T>>
    {
        public Task Handle(Envelope<T> notification, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}

namespace Caesar.Tests.Fixtures.Benign
{
    /// <summary>A generic type that touches no Caesar interface; scanning must ignore it silently.</summary>
    public sealed class Box<T>
    {
        public T? Value { get; set; }
    }
}
