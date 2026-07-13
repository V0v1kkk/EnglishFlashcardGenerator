using AutoGen.AzureAIInference;
using AutoGen.AzureAIInference.Extension;
using AutoGen.Core;
using AutoGen.OpenAI;
using AutoGen.OpenAI.Extension;
using Azure.AI.Inference;
using OpenAI.Chat;

namespace EnglishFlashcardGenerator;

public class FlashcardReviewerAgent : AgentBase
{
    private readonly string _instruction;
    private readonly string _name;
    
    public FlashcardReviewerAgent(float temperature = 1.0f, int maxTokens = 16384)
        : base(temperature, maxTokens)
    {
        Introduction = "I am an English learning methodical expert. I will review the flashcards and provide feedback.";
        
        _name = "FlashcardReviewerAgent";
        
        _instruction =  """
                        You are an English learning methodological expert. You have a lot of expertise in English language teaching.
                        You need to review the flashcards based on a student notes and provide feedback.
                        Be sure that flashcards are good for memorization and factually correct for learning English language.
                        
                        Be strict in your review and provide detailed feedback.
                        
                        Flashcard might be single direction (basic) or double sided (reversed). It means that flip side of the card might be a question or an answer.
                        It make sense to create double sided cards for words or phrases translation tasks.
                        You HAVE criticise using reversed cards instead of basic if it meaningless.
                        
                        Try to ask yourself questions from the flashcards, score how easy it is to answer them and how easy to figure out what the question is about and provide feedback.
                        
                        Criticise using reversed/double-sided cards instead of basic if the cards back side is not meaningful as a question or an answer.
                        
                        Criticise examples in reversed/double-sided cards if they spoil the answer.
                        Imagine that you have to answer the question on the back side of the card.
                        In some cases it's better to put an example on the front side of the card.
                        Example of spoiled answer:
                        ```
                        **Front:** How do W-questions change in indirect question form?
                        Examples:
                        - *Where is the library?* becomes *Can you tell me where the library is?*
                        **Back:** In indirect W-questions, the second part of the sentence changes.
                        **Double sided:** no
                        ```
                        Good version:
                        ```
                        **Front:** How do W-questions change in indirect question form?
                        **Back:** In indirect W-questions, the second part of the sentence changes. For example: *Where is the library?* becomes *Can you tell me where the library is?*
                        **Double sided:** no
                        ```
                        
                        You must try to answer question single direction (basic) cards and both questions and answers double sided (reversed) cards yourself and provide feedback.
                        
                        Answer 'OK!' in the LAST line of your answer if new flashcards are good for memorization.
                        Otherwise provide feedback and suggestions.
                        Don't put 'OK!' into the last line if you have any suggestions and they haven't applied yet.
                        """;
    }

    public override string Introduction { get; }
    
    public override IAgent CreateOpenAiAgent(ChatClient client)
    {
        return new OpenAIChatAgent(
                chatClient: client,
                name: _name,
                temperature: Temperature,
                systemMessage: _instruction)
                .RegisterMessageConnector()
                .RegisterPrintMessage();
    }

    public override IAgent CreateAzureAgent(ChatCompletionsClient client, string modelName)
    {
        return new ChatCompletionsClientAgent(
                chatCompletionsClient: client,
                name: _name,
                modelName: modelName,
                systemMessage: _instruction,
                temperature: Temperature,
                maxTokens: MaxTokens)
                .RegisterMessageConnector()
                .RegisterPrintMessage();
    }
}