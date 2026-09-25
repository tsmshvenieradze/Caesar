using Caesar.Pipeline;
using Microsoft.Extensions.DependencyInjection;

// Contracts that no earlier test pinned: processors run inside the exception stages, a command never throws from Send
// itself, and a command handler the container cannot create reports the container's own error.

namespace Caesar.Tests.Regression.PipelineContract.ProcessorFailures
{
    public sealed class ValidationException(string message) : Exception(message);

    public sealed record Checked(Journal Journal, bool FailBefore, bool FailAfter) : IRequest<string>;

    public sealed class CheckedHandler : IRequestHandler<Checked, string>
    {
        public Task<string> Handle(Checked request, CancellationToken cancellationToken)
        {
            request.Journal.Add("handler");
            return Task.FromResult("ok");
        }
    }

    public sealed class ValidatingPre : IRequestPreProcessor<Checked>
    {
        public Task Process(Checked request, CancellationToken cancellationToken)
            => request.FailBefore ? Task.FromException(new ValidationException("before")) : Task.CompletedTask;
    }

    public sealed class AuditingPost : IRequestPostProcessor<Checked, string>
    {
        public Task Process(Checked request, string response, CancellationToken cancellationToken)
            => request.FailAfter ? Task.FromException(new ValidationException("after")) : Task.CompletedTask;
    }

    public sealed class MapValidation : IRequestExceptionHandler<Checked, string, ValidationException>
    {
        public Task Handle(Checked request, ValidationException exception, RequestExceptionHandlerState<string> state, CancellationToken cancellationToken)
        {
            request.Journal.Add("mapped:" + exception.Message);
            state.SetHandled("invalid");
            return Task.CompletedTask;
        }
    }

    public sealed class LogValidation : IRequestExceptionAction<Checked, ValidationException>
    {
        public Task Execute(Checked request, ValidationException exception, CancellationToken cancellationToken)
        {
            request.Journal.Add("logged:" + exception.Message);
            return Task.CompletedTask;
        }
    }

    public class ProcessorFailureTests
    {
        private static ServiceProvider Build(bool twoCalls)
        {
            var ns = typeof(ProcessorFailureTests).Namespace!;
            var services = new ServiceCollection();
            if (twoCalls)
            {
                // Processors in one module, exception handling in another: the order must not depend on the calls.
                services.AddCaesar(cfg =>
                {
                    cfg.RegisterServicesFromAssemblyContaining<ProcessorFailureTests>();
                    cfg.TypeEvaluator = t => t.Namespace == ns && t.Name is nameof(CheckedHandler) or nameof(ValidatingPre) or nameof(AuditingPost);
                    cfg.RequestExceptionActionProcessorStrategy = DependencyInjection.RequestExceptionActionProcessorStrategy.ApplyForAllExceptions;
                });
                services.AddCaesar(cfg =>
                {
                    cfg.RegisterServicesFromAssemblyContaining<ProcessorFailureTests>();
                    cfg.TypeEvaluator = t => t.Namespace == ns && t.Name is nameof(MapValidation) or nameof(LogValidation);
                });
            }
            else
            {
                services.AddCaesar(cfg =>
                {
                    cfg.RegisterServicesFromAssemblyContaining<ProcessorFailureTests>();
                    cfg.TypeEvaluator = t => t.Namespace == ns;
                    cfg.RequestExceptionActionProcessorStrategy = DependencyInjection.RequestExceptionActionProcessorStrategy.ApplyForAllExceptions;
                });
            }

            return services.BuildServiceProvider();
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task A_failing_pre_processor_is_seen_by_exception_actions_and_handlers(bool twoCalls)
        {
            var journal = new Journal();
            await using var provider = Build(twoCalls);
            await using var scope = provider.CreateAsyncScope();

            var response = await scope.ServiceProvider.GetRequiredService<ISender>().Send(new Checked(journal, FailBefore: true, FailAfter: false));

            Assert.Equal("invalid", response);
            Assert.Equal(["logged:before", "mapped:before"], journal.Entries);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task A_failing_post_processor_is_seen_by_exception_actions_and_handlers(bool twoCalls)
        {
            var journal = new Journal();
            await using var provider = Build(twoCalls);
            await using var scope = provider.CreateAsyncScope();

            var response = await scope.ServiceProvider.GetRequiredService<ISender>().Send(new Checked(journal, FailBefore: false, FailAfter: true));

            Assert.Equal("invalid", response);
            Assert.Equal(["handler", "logged:after", "mapped:after"], journal.Entries);
        }
    }
}

namespace Caesar.Tests.Regression.PipelineContract.CommandFailures
{
    public sealed class ForbiddenException() : Exception("forbidden");

    public interface IGuarded;

    public sealed record Archive : IRequest, IGuarded;

    public sealed class ArchiveHandler : IRequestHandler<Archive>
    {
        public Task Handle(Archive request, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    /// <summary>A non-async authorization behavior that throws before returning a task.</summary>
    public sealed class Authorize<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
        where TRequest : IGuarded
    {
        public Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
            => throw new ForbiddenException();
    }

    public class CommandFailureTests
    {
        [Fact]
        public async Task A_command_whose_behavior_throws_synchronously_returns_a_faulted_task()
        {
            await using var provider = TestHost.Build<CommandFailureTests>(cfg => cfg.AddOpenBehavior(typeof(Authorize<,>)));
            var sender = provider.GetRequiredService<ISender>();

            var task = sender.Send(new Archive());

            Assert.True(task.IsFaulted);
            await Assert.ThrowsAsync<ForbiddenException>(() => task);
        }

        [Fact]
        public async Task A_command_sent_as_object_whose_behavior_throws_synchronously_returns_a_faulted_task()
        {
            await using var provider = TestHost.Build<CommandFailureTests>(cfg => cfg.AddOpenBehavior(typeof(Authorize<,>)));
            var sender = provider.GetRequiredService<ISender>();

            var task = sender.Send((object)new Archive());

            Assert.True(task.IsFaulted);
            await Assert.ThrowsAsync<ForbiddenException>(() => task);
        }
    }
}

namespace Caesar.Tests.Regression.PipelineContract.CommandResolution
{
    public interface IMissingDependency;

    public sealed record Void : IRequest;

    public sealed class VoidHandler(IMissingDependency dependency) : IRequestHandler<Void>
    {
        public Task Handle(Void request, CancellationToken cancellationToken) => Task.FromResult(dependency);
    }

    public sealed record UnitShaped : IRequest;

    public sealed class UnitShapedHandler(IMissingDependency dependency) : IRequestHandler<UnitShaped, Unit>
    {
        public Task<Unit> Handle(UnitShaped request, CancellationToken cancellationToken) => Task.FromResult(dependency is null ? Unit.Value : Unit.Value);
    }

    public class CommandResolutionTests
    {
        [Fact]
        public async Task A_void_command_handler_with_a_missing_dependency_reports_the_container_error()
        {
            await using var provider = TestHost.Build<CommandResolutionTests>();

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => provider.GetRequiredService<ISender>().Send(new Void()));

            Assert.Contains(nameof(IMissingDependency), exception.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("No handler was found", exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task A_unit_command_handler_with_a_missing_dependency_reports_the_container_error()
        {
            await using var provider = TestHost.Build<CommandResolutionTests>();

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => provider.GetRequiredService<ISender>().Send(new UnitShaped()));

            Assert.Contains(nameof(IMissingDependency), exception.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("No handler was found", exception.Message, StringComparison.Ordinal);
        }
    }
}
