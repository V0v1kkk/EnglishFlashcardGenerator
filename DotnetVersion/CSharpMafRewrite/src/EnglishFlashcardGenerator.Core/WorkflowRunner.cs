using Microsoft.Agents.AI.Workflows;
using System.Text;

namespace EnglishFlashcardGenerator.Core;

public static class WorkflowRunner
{
    public static async Task<T> RunAsync<T>(Workflow workflow, object input, CancellationToken cancellationToken = default)
    {
        var typedInput = (dynamic)input;
        Run run = await InProcessExecution.RunAsync(workflow, typedInput, cancellationToken: cancellationToken).AsTask().ConfigureAwait(false);
        var events = run.NewEvents.ToArray();
        var output = events.OfType<WorkflowOutputEvent>()
            .Where(e => e.Is<T>())
            .Select(e => e.As<T>())
            .LastOrDefault();

        if (output is not null)
        {
            return output;
        }

        var failures = DescribeFailures(events).ToArray();
        if (failures.Length > 0)
        {
            throw new WorkflowExecutionException(
                $"Workflow failed before producing {typeof(T).Name}. {string.Join(" | ", failures.Select(f => f.Message))}",
                failures.Select(f => f.Exception).FirstOrDefault(e => e is not null));
        }

        var eventTypes = string.Join(", ", events.Select(e => e.GetType().Name).Distinct());
        throw new WorkflowExecutionException($"Workflow completed without a {typeof(T).Name} output. Events: {eventTypes}");
    }

    private static IEnumerable<(string Message, Exception? Exception)> DescribeFailures(IEnumerable<WorkflowEvent> events)
    {
        foreach (var evt in events)
        {
            switch (evt)
            {
                case SubworkflowErrorEvent subworkflowError:
                    yield return ($"SubworkflowErrorEvent[{subworkflowError.SubworkflowId}]: {DescribeException(subworkflowError.Data as Exception)}", subworkflowError.Data as Exception);
                    break;
                case WorkflowErrorEvent workflowError:
                    yield return ($"WorkflowErrorEvent: {DescribeException(workflowError.Exception)}", workflowError.Exception);
                    break;
                case ExecutorFailedEvent executorFailed:
                    yield return ($"ExecutorFailedEvent[{executorFailed.ExecutorId}]: {DescribeException(executorFailed.Data as Exception)}", executorFailed.Data as Exception);
                    break;
            }
        }
    }

    private static string DescribeException(Exception? exception)
    {
        if (exception is null)
        {
            return "no exception details";
        }

        var builder = new StringBuilder();
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (builder.Length > 0)
            {
                builder.Append(" -> ");
            }

            builder.Append(current.GetType().Name).Append(": ").Append(current.Message);
        }

        return builder.ToString();
    }
}
