namespace Caesar.Sample.Application.Behaviors;

/// <summary>Minimal validator abstraction; swap for FluentValidation in a real project.</summary>
public interface IValidator<in TRequest>
{
    IEnumerable<string> Validate(TRequest request);
}

public sealed class ValidationException(IReadOnlyList<string> errors) : Exception("Request validation failed.")
{
    public IReadOnlyList<string> Errors { get; } = errors;
}

/// <summary>Runs every registered validator for the request and short-circuits when any error is found.</summary>
public sealed class ValidationBehavior<TRequest, TResponse>(IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var errors = validators.SelectMany(v => v.Validate(request)).ToList();
        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }

        return await next(cancellationToken);
    }
}
