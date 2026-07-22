namespace EnglishFlashcardGenerator.Core.Agents;

/// <summary>
/// Thrown when the LLM provider returns a fatal error (e.g. 401 Unauthorized, 403 Forbidden, 402 Payment Required, or Quota Exceeded)
/// indicating that the workflow should be immediately aborted rather than retried or gracefully degraded.
/// </summary>
public sealed class FatalProviderException : Exception
{
    public FatalProviderException(string message) : base(message)
    {
    }

    public FatalProviderException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
