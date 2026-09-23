using Caesar.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace Caesar.Tests;

/// <summary>
/// Builds an isolated container that only scans the types living in the caller's namespace,
/// so handlers from one scenario never leak into another.
/// </summary>
public static class TestHost
{
    public static TestProvider Build<TMarker>(Action<CaesarServiceConfiguration>? configure = null, Action<IServiceCollection>? services = null)
    {
        var ns = typeof(TMarker).Namespace!;
        var collection = new ServiceCollection();
        services?.Invoke(collection);

        collection.AddCaesar(cfg =>
        {
            cfg.RegisterServicesFromAssemblyContaining<TMarker>();
            cfg.TypeEvaluator = t => t.Namespace == ns;
            configure?.Invoke(cfg);
        });

        return new TestProvider(collection.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true }));
    }
}

/// <summary>
/// A root container plus an ambient scope. Caesar registers the mediator as scoped, so tests resolve
/// through a scope the way a request would; <see cref="Root"/> still reaches the root container.
/// </summary>
public sealed class TestProvider : IServiceProvider, IDisposable, IAsyncDisposable
{
    private readonly ServiceProvider _root;
    private readonly AsyncServiceScope _scope;

    public TestProvider(ServiceProvider root)
    {
        _root = root;
        _scope = root.CreateAsyncScope();
    }

    /// <summary>The root container, for assertions that must bypass the ambient scope.</summary>
    public IServiceProvider Root => _root;

    public object? GetService(Type serviceType) => _scope.ServiceProvider.GetService(serviceType);

    public void Dispose()
    {
        _scope.Dispose();
        _root.Dispose();
        GC.SuppressFinalize(this);
    }

    public async ValueTask DisposeAsync()
    {
        await _scope.DisposeAsync();
        await _root.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}

/// <summary>Anything that carries a <see cref="Journal"/>; lets open-generic behaviors constrain on it.</summary>
public interface IJournaled
{
    Journal Journal { get; }
}

/// <summary>Records execution order across handlers and behaviors in a scenario.</summary>
public sealed class Journal
{
    private readonly List<string> _entries = [];
    private readonly Lock _lock = new();

    public IReadOnlyList<string> Entries
    {
        get
        {
            lock (_lock)
            {
                return [.. _entries];
            }
        }
    }

    public void Add(string entry)
    {
        lock (_lock)
        {
            _entries.Add(entry);
        }
    }
}
