using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using Caesar.Tests.Fixtures.Benign;
using Microsoft.Extensions.DependencyInjection;

namespace Caesar.Tests.Regression.CollectibleTypes;

/// <summary>
/// A plugin context that loads only the fixture assembly itself; everything it references, Caesar.Abstractions
/// included, comes from the default context, so the plugin's messages implement the same Caesar interfaces.
/// </summary>
internal sealed class PluginContext() : AssemblyLoadContext("caesar-plugin", isCollectible: true)
{
    protected override Assembly? Load(AssemblyName assemblyName) => null;
}

/// <summary>
/// The tests that unload a plugin context. They force collections and unload assemblies, which disturbs tests that
/// measure allocations on their own thread, so they run on their own, after the parallel tests.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class CollectibleContextCollection
{
    public const string Name = "Collectible contexts";
}

[Collection(CollectibleContextCollection.Name)]
public class CollectibleContextTests
{
    private const string PluginNamespace = "Caesar.Tests.Fixtures.Collectible";

    [Fact]
    public void Dispatching_messages_from_a_collectible_context_does_not_keep_it_alive()
    {
        var context = DispatchInCollectibleContext();

        for (var i = 0; context.IsAlive && i < 50; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        Assert.False(context.IsAlive, "The unloaded plugin context is still reachable after dispatching its messages.");
    }

    /// <summary>Everything that references the plugin stays in this frame, so only the returned weak reference survives it.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference DispatchInCollectibleContext()
    {
        var context = new PluginContext();
        try
        {
            Dispatch(context.LoadFromAssemblyPath(typeof(Box<>).Assembly.Location));
        }
        finally
        {
            context.Unload();
        }

        return new WeakReference(context);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Dispatch(Assembly plugin)
    {
        var services = new ServiceCollection();
        services.AddCaesar(cfg =>
        {
            cfg.RegisterServicesFromAssembly(plugin);
            cfg.TypeEvaluator = static t => t.Namespace == PluginNamespace;
        });

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        var echo = (IRequest<string>)Create(plugin, "Echo", "hi");
        Assert.Equal("hi", mediator.Send(echo).GetAwaiter().GetResult());
        Assert.Equal("hi", mediator.Send((object)echo).GetAwaiter().GetResult());

        var touch = (IRequest)Create(plugin, "Touch");
        mediator.Send(touch).GetAwaiter().GetResult();

        mediator.Publish(Create(plugin, "Announced")).GetAwaiter().GetResult();

        var numbers = (IStreamRequest<int>)Create(plugin, "Numbers", 2);
        Assert.Equal(2, Drain(mediator.CreateStream(numbers)));
        Assert.Equal(2, Drain(mediator.CreateStream((object)numbers)));
    }

    private static object Create(Assembly plugin, string name, params object[] arguments)
        => Activator.CreateInstance(plugin.GetType($"{PluginNamespace}.{name}", throwOnError: true)!, arguments)!;

    /// <summary>The fixture's streams complete synchronously, so they can be drained without blocking on real work.</summary>
    private static int Drain<T>(IAsyncEnumerable<T> stream)
    {
        var count = 0;
        var enumerator = stream.GetAsyncEnumerator();
        try
        {
            while (enumerator.MoveNextAsync().AsTask().GetAwaiter().GetResult())
            {
                count++;
            }
        }
        finally
        {
            enumerator.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        return count;
    }
}
