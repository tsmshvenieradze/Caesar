using Caesar.DependencyInjection;
using Caesar.Pipeline;
using Microsoft.Extensions.DependencyInjection;

namespace Caesar.Wrappers;

/// <summary>One of Caesar's built-in pipeline stages.</summary>
internal enum BuiltInStage : byte
{
    ExceptionActions,
    ExceptionHandlers,
    PreProcessors,
    PostProcessors,
}

/// <summary>
/// What the pipeline of one closed request type looks like in one container, worked out once from its
/// <see cref="CaesarRegistry"/>: which built-in stages wrap the handler, and which handler registrations are worth
/// asking the container for. It is an open-generic singleton, so the container keeps one per request type.
/// </summary>
/// <typeparam name="TRequest">The request type.</typeparam>
/// <typeparam name="TResponse">The response type.</typeparam>
internal sealed class RequestPipelineShape<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly BuiltInStage[] _stages;
    private readonly bool _bypassCallerCancellation;

    /// <summary>Creates the shape. Called by the container.</summary>
    /// <param name="registry">What the container's service collection registers.</param>
    public RequestPipelineShape(CaesarRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);

        _stages = ComputeStages(registry);
        _bypassCallerCancellation = registry.BypassExceptionHandlingOnCallerCancellation;
        OuterBehaviorCount = _stages.Length == 0 ? 0 : registry.CountBehaviorsRegisteredBeforeFirstCall(typeof(IPipelineBehavior<TRequest, TResponse>));

        var handler = registry.GetAvailability(typeof(IRequestHandler<TRequest, TResponse>));
        ProbeHandler = handler != ServiceAvailability.ConstraintViolation;
        HasHandler = handler == ServiceAvailability.Registered;

        if (typeof(TResponse) == typeof(Unit) && typeof(IRequest).IsAssignableFrom(typeof(TRequest)))
        {
            // IRequestHandler<TRequest> wins when both command handler shapes are registered. When only the Unit shape
            // is, skip the lookup of the other one. When neither is known, ask for both: the container may have been
            // populated some other way.
            var voidHandler = registry.GetAvailability(typeof(IRequestHandler<>).MakeGenericType(typeof(TRequest)));
            ProbeVoidHandler = voidHandler == ServiceAvailability.Registered
                || (voidHandler == ServiceAvailability.None && handler != ServiceAvailability.Registered);
        }
    }

    /// <summary><see langword="true"/> when at least one built-in stage wraps the handler.</summary>
    public bool HasStages => _stages.Length != 0;

    /// <summary>The number of built-in stages.</summary>
    public int StageCount => _stages.Length;

    /// <summary>
    /// How many of the resolved pipeline behaviors, counted from the first, run outside the built-in stages: those
    /// registered before the first <c>AddCaesar</c> call, which is where the built-in behaviors used to be appended.
    /// </summary>
    public int OuterBehaviorCount { get; }

    /// <summary>
    /// <see langword="false"/> when the only registration for <see cref="IRequestHandler{TRequest, TResponse}"/> is an
    /// open generic whose constraints reject <typeparamref name="TRequest"/>, so asking the container would throw.
    /// </summary>
    public bool ProbeHandler { get; }

    /// <summary><see langword="true"/> when <see cref="IRequestHandler{TRequest, TResponse}"/> itself is registered and can be resolved.</summary>
    public bool HasHandler { get; }

    /// <summary>For a command, whether to ask the container for <see cref="IRequestHandler{TRequest}"/> first.</summary>
    public bool ProbeVoidHandler { get; }

    /// <summary>Creates the built-in stage at <paramref name="index"/>, outermost first, resolving what it runs from <paramref name="serviceProvider"/>.</summary>
    public IPipelineBehavior<TRequest, TResponse> CreateStage(int index, IServiceProvider serviceProvider) => _stages[index] switch
    {
        BuiltInStage.ExceptionActions => new RequestExceptionActionProcessorBehavior<TRequest, TResponse>(serviceProvider, _bypassCallerCancellation),
        BuiltInStage.ExceptionHandlers => new RequestExceptionProcessorBehavior<TRequest, TResponse>(serviceProvider, _bypassCallerCancellation),
        BuiltInStage.PreProcessors => new RequestPreProcessorBehavior<TRequest, TResponse>(serviceProvider.GetServices<IRequestPreProcessor<TRequest>>()),
        _ => new RequestPostProcessorBehavior<TRequest, TResponse>(serviceProvider.GetServices<IRequestPostProcessor<TRequest, TResponse>>()),
    };

    /// <summary>
    /// The stages that have something to run for this request, in the documented order, outermost first:
    /// exception actions and exception handlers (in the order the strategy asks for), pre-processors, post-processors.
    /// A stage whose behavior the user registered as an ordinary pipeline behavior is left out, so it never runs twice.
    /// </summary>
    private static BuiltInStage[] ComputeStages(CaesarRegistry registry)
    {
        var behaviorService = typeof(IPipelineBehavior<TRequest, TResponse>);

        bool Applies(bool used, Type builtInBehavior) => used && !registry.IsRegisteredAsPipelineBehavior(behaviorService, builtInBehavior);

        // Exception handlers and actions are resolved per thrown exception type, which is unknown here: any closed
        // registration for this request counts, and so does any open-generic one.
        var actions = Applies(
            registry.HasAnyStartingWith(typeof(IRequestExceptionAction<,>), typeof(IRequestExceptionAction<TRequest, Exception>), typeof(TRequest)),
            typeof(RequestExceptionActionProcessorBehavior<TRequest, TResponse>));
        var handlers = Applies(
            registry.HasAnyStartingWith(typeof(IRequestExceptionHandler<,,>), typeof(IRequestExceptionHandler<TRequest, TResponse, Exception>), typeof(TRequest), typeof(TResponse)),
            typeof(RequestExceptionProcessorBehavior<TRequest, TResponse>));
        var pre = Applies(registry.HasAny(typeof(IRequestPreProcessor<TRequest>)), typeof(RequestPreProcessorBehavior<TRequest, TResponse>));
        var post = Applies(registry.HasAny(typeof(IRequestPostProcessor<TRequest, TResponse>)), typeof(RequestPostProcessorBehavior<TRequest, TResponse>));

        var stages = new List<BuiltInStage>(4);
        if (registry.RequestExceptionActionProcessorStrategy == RequestExceptionActionProcessorStrategy.ApplyForUnhandledExceptions)
        {
            AddIf(stages, actions, BuiltInStage.ExceptionActions);
            AddIf(stages, handlers, BuiltInStage.ExceptionHandlers);
        }
        else
        {
            AddIf(stages, handlers, BuiltInStage.ExceptionHandlers);
            AddIf(stages, actions, BuiltInStage.ExceptionActions);
        }

        AddIf(stages, pre, BuiltInStage.PreProcessors);
        AddIf(stages, post, BuiltInStage.PostProcessors);
        return [.. stages];
    }

    private static void AddIf(List<BuiltInStage> stages, bool condition, BuiltInStage stage)
    {
        if (condition)
        {
            stages.Add(stage);
        }
    }
}
