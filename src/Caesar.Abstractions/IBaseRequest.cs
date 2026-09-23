namespace Caesar;

/// <summary>
/// Marker interface shared by every request that is dispatched through <see cref="ISender"/>.
/// Do not implement this directly; use <see cref="IRequest"/>, <see cref="IRequest{TResponse}"/>
/// or <see cref="IStreamRequest{TResponse}"/>.
/// </summary>
public interface IBaseRequest;
