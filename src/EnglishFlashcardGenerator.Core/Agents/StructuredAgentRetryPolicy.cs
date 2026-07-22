using System.Text.Json;

namespace EnglishFlashcardGenerator.Core.Agents;

public static class StructuredAgentRetryPolicy
{
    public static async ValueTask<T> RunAsync<T>(
        string agentName,
        Func<int, CancellationToken, ValueTask<T>> operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);

        const int maxAttempts = 2;
        Exception? lastFailure = null;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                return await operation(attempt, cancellationToken).ConfigureAwait(false);
            }
            catch (JsonException ex) when (attempt < maxAttempts)
            {
                lastFailure = ex;
            }
            catch (InvalidOperationException ex) when (IsStructuredResultFailure(ex) && attempt < maxAttempts)
            {
                lastFailure = ex;
            }
            catch (JsonException ex)
            {
                throw new AgentBoundaryException(agentName, typeof(T), "provider returned invalid or incomplete structured JSON after retry", ex);
            }
            catch (InvalidOperationException ex) when (IsStructuredResultFailure(ex))
            {
                throw new AgentBoundaryException(agentName, typeof(T), "provider returned an invalid structured result after retry", ex);
            }
            catch (FatalProviderException)
            {
                throw;
            }
            catch (AgentBoundaryException)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new AgentBoundaryException(agentName, typeof(T), "provider call failed before a structured result was available", ex);
            }
        }

        throw new AgentBoundaryException(
            agentName,
            typeof(T),
            "provider returned invalid or incomplete structured JSON after retry",
            lastFailure ?? new InvalidOperationException("Structured result failed without an exception."));
    }

    private static bool IsStructuredResultFailure(InvalidOperationException ex) =>
        ex.InnerException is JsonException ||
        ex.Message.Contains("json", StringComparison.OrdinalIgnoreCase) ||
        ex.Message.Contains("structured", StringComparison.OrdinalIgnoreCase) ||
        ex.Message.Contains("result", StringComparison.OrdinalIgnoreCase) ||
        ex.Message.Contains("deserialize", StringComparison.OrdinalIgnoreCase);
}
