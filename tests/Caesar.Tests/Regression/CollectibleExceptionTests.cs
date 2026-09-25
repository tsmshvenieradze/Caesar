using System.Reflection;
using System.Runtime.CompilerServices;
using Caesar.Pipeline;
using Caesar.Tests.Fixtures.Benign;
using Caesar.Tests.Regression.CollectibleTypes;
using Microsoft.Extensions.DependencyInjection;

namespace Caesar.Tests.Regression.CollectibleExceptions;

/// <summary>A request of the host's own that fails with the exception it carries, here one whose type is the plugin's.</summary>
public sealed record Relay(Exception Failure, bool Recover, Journal Journal) : IRequest<string>;

public sealed class RelayHandler : IRequestHandler<Relay, string>
{
    public Task<string> Handle(Relay request, CancellationToken cancellationToken) => Task.FromException<string>(request.Failure);
}

public sealed class RelayRecovery : IRequestExceptionHandler<Relay, string, Exception>
{
    public Task Handle(Relay request, Exception exception, RequestExceptionHandlerState<string> state, CancellationToken cancellationToken)
    {
        request.Journal.Add($"handler:{exception.GetType().Name}");
        if (request.Recover)
        {
            state.SetHandled("recovered");
        }

        return Task.CompletedTask;
    }
}

public sealed class RelayObserver : IRequestExceptionAction<Relay, Exception>
{
    public Task Execute(Relay request, Exception exception, CancellationToken cancellationToken)
    {
        request.Journal.Add($"action:{exception.GetType().Name}");
        return Task.CompletedTask;
    }
}

/// <summary>
/// The exception behaviors cache what they need per thrown exception type. An exception type from a collectible
/// context must not keep that context alive, whether the failing request is the plugin's or the host's.
/// </summary>
[Collection(CollectibleContextCollection.Name)]
public class CollectibleExceptionTests
{
    private const string PluginNamespace = "Caesar.Tests.Fixtures.Collectible";

    [Fact]
    public void A_plugin_request_failing_with_a_plugin_exception_does_not_keep_the_context_alive()
        => AssertUnloads(static (plugin, mediator) =>
        {
            var log = new List<string>();

            var recovered = (IRequest<string>)Create(plugin, "Fail", true, log);
            Assert.Equal("recovered", mediator.Send(recovered).GetAwaiter().GetResult());

            var unhandled = (IRequest<string>)Create(plugin, "Fail", false, log);
            var thrown = Assert.ThrowsAny<Exception>(() => mediator.Send(unhandled).GetAwaiter().GetResult());

            Assert.Equal("PluginException", thrown.GetType().Name);
            Assert.Equal(["handler", "handler", "action"], log);
        });

    [Fact]
    public void A_host_request_failing_with_a_plugin_exception_does_not_keep_the_context_alive()
        => AssertUnloads(static (plugin, mediator) =>
        {
            var journal = new Journal();
            var failure = (Exception)Create(plugin, "PluginException", "plugin failed");

            Assert.Equal("recovered", mediator.Send(new Relay(failure, Recover: true, journal)).GetAwaiter().GetResult());

            var thrown = Assert.ThrowsAny<Exception>(() => mediator.Send(new Relay(failure, Recover: false, journal)).GetAwaiter().GetResult());

            Assert.Same(failure, thrown);
            Assert.Equal(["handler:PluginException", "handler:PluginException", "action:PluginException"], journal.Entries);
        });

    private static void AssertUnloads(Action<Assembly, IMediator> scenario)
    {
        var context = RunInCollectibleContext(scenario);

        for (var i = 0; context.IsAlive && i < 50; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        Assert.False(context.IsAlive, "The unloaded plugin context is still reachable after a request failed with one of its exception types.");
    }

    /// <summary>Everything that references the plugin stays in this frame, so only the returned weak reference survives it.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference RunInCollectibleContext(Action<Assembly, IMediator> scenario)
    {
        var context = new PluginContext();
        try
        {
            Run(context.LoadFromAssemblyPath(typeof(Box<>).Assembly.Location), scenario);
        }
        finally
        {
            context.Unload();
        }

        return new WeakReference(context);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Run(Assembly plugin, Action<Assembly, IMediator> scenario)
    {
        var services = new ServiceCollection();
        services.AddCaesar(cfg =>
        {
            cfg.RegisterServicesFromAssembly(plugin);
            cfg.RegisterServicesFromAssemblyContaining<CollectibleExceptionTests>();
            cfg.TypeEvaluator = static t => t.Namespace == PluginNamespace || t.Namespace == typeof(CollectibleExceptionTests).Namespace;
        });

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        scenario(plugin, scope.ServiceProvider.GetRequiredService<IMediator>());
    }

    private static object Create(Assembly plugin, string name, params object[] arguments)
        => Activator.CreateInstance(plugin.GetType($"{PluginNamespace}.{name}", throwOnError: true)!, arguments)!;
}
