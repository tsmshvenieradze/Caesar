namespace Caesar;

/// <summary>
/// Combines <see cref="ISender"/> and <see cref="IPublisher"/>.
/// Prefer injecting the narrower interface when a component only sends or only publishes.
/// </summary>
public interface IMediator : ISender, IPublisher;
