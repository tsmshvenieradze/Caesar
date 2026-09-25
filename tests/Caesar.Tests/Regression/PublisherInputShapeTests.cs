using System.Collections.ObjectModel;
using Caesar.NotificationPublishers;

namespace Caesar.Tests.Regression.PublisherInputShape;

public sealed record Shaped : INotification;

/// <summary>
/// The built-in publishers take any <see cref="IEnumerable{T}"/>, not only the array the mediator passes.
/// Guards the array and list fast paths added for F22.
/// </summary>
public class PublisherInputShapeTests
{
    public static TheoryData<Type, string> Cases()
    {
        var data = new TheoryData<Type, string>();
        foreach (var publisher in new[] { typeof(ForeachAwaitPublisher), typeof(TaskWhenAllPublisher), typeof(ForeachAwaitContinueOnFailurePublisher) })
        {
            foreach (var shape in new[] { "array", "list", "read-only", "lazy" })
            {
                data.Add(publisher, shape);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Every_executor_runs_in_order_whatever_the_sequence_type(Type publisherType, string shape)
    {
        var publisher = (INotificationPublisher)Activator.CreateInstance(publisherType)!;
        var journal = new Journal();
        var executors = Enumerable.Range(0, 3)
            .Select(i => new NotificationHandlerExecutor(i, (_, _) =>
            {
                journal.Add($"e{i}");
                return Task.CompletedTask;
            }))
            .ToArray();

        await publisher.Publish(Shape(executors, shape), new Shaped(), CancellationToken.None);

        Assert.Equal(["e0", "e1", "e2"], journal.Entries);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task An_empty_sequence_completes(Type publisherType, string shape)
    {
        var publisher = (INotificationPublisher)Activator.CreateInstance(publisherType)!;

        await publisher.Publish(Shape([], shape), new Shaped(), CancellationToken.None);
    }

    [Theory]
    [InlineData(typeof(ForeachAwaitPublisher))]
    [InlineData(typeof(TaskWhenAllPublisher))]
    [InlineData(typeof(ForeachAwaitContinueOnFailurePublisher))]
    public async Task A_null_sequence_faults_the_returned_task(Type publisherType)
    {
        var publisher = (INotificationPublisher)Activator.CreateInstance(publisherType)!;

        var task = publisher.Publish(null!, new Shaped(), CancellationToken.None);

        await Assert.ThrowsAsync<ArgumentNullException>(() => task);
    }

    private static IEnumerable<NotificationHandlerExecutor> Shape(NotificationHandlerExecutor[] executors, string shape) => shape switch
    {
        "array" => executors,
        "list" => new List<NotificationHandlerExecutor>(executors),
        "read-only" => new ReadOnlyCollection<NotificationHandlerExecutor>(executors),
        _ => Lazy(executors),
    };

    private static IEnumerable<NotificationHandlerExecutor> Lazy(NotificationHandlerExecutor[] executors)
    {
        foreach (var executor in executors)
        {
            yield return executor;
        }
    }
}
