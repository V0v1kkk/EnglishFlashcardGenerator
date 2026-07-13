namespace EnglishFlashcardGenerator.Core.Agents;

public sealed class AgentBoundaryException : Exception
{
    public AgentBoundaryException(string agentName, Type outputType, string message, Exception innerException)
        : base($"Agent boundary failure in {agentName} while reading {outputType.Name}: {message}", innerException)
    {
        AgentName = agentName;
        OutputType = outputType;
    }

    public string AgentName { get; }
    public Type OutputType { get; }
}
