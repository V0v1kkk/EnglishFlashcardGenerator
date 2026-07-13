namespace EnglishFlashcardGenerator.Core;

public sealed class WorkflowExecutionException : Exception
{
    public WorkflowExecutionException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
