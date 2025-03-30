using AutoGen.AzureAIInference;
using AutoGen.AzureAIInference.Extension;
using AutoGen.Core;
using AutoGen.OpenAI;
using AutoGen.OpenAI.Extension;
using Azure.AI.Inference;
using OpenAI.Chat;

namespace EnglishFlashcardGenerator;

public class FlashCardExtractorAgent : AgentBase
{
    private readonly string _instruction;
    private readonly string _name;
    private readonly BinaryData _jsonSchema;

    public FlashCardExtractorAgent(float temperature = 0.2f, int maxTokens = 16384)
        : base(temperature, maxTokens)
    {
        Introduction = """
                       I will format questions and answers into flashcards.
                       I can make flashcard from question and answer pair.
                       And also can make reversed flashcard where answer will be a question as well.
                       Multiline flashcards are also supported.
                       Tell me what you need and I will generate flashcards for you.
                       """;
        
        _name = "FlashCardExtractorAgent";
        
        _instruction = """
                        You are experienced in memorization techniques and in flashcard creation.
                        Your task is to format questions and answers from the English teacher into flashcards.
                        
                        In the request usually is already specified if it is a basic card (single sided) or reversed card (double sided).
                        
                        Format the request into flashcards collection according the schema. 
                        
                        If it's provided examples for a card, put examples to the end of the card with markdown italic: *Example sentence.*
                        Example of a basic multi-line card with an example:
                        
                        If a card contains something more complicated than translation then express the task in the beginning of the card. 
                        
                        In case of simple word or phrase translation task on reversed card you must skip the task description and put only the translations on front and back.  
                        
                        Eliminate any blank lines in the card.
                        
                        Double check that examples for one side of the card don't spoil the answer on the other side.
                        
                        IMPORTANT: Your response MUST be a valid JSON object with the exact structure shown below:
                        {
                          "FlashCards": [
                            {
                              "Front": "question or front side text",
                              "Back": "answer or back side text",
                              "IsReversed": true/false
                            },
                            ...more cards...
                          ]
                        }
                        
                        Do NOT include any additional fields, metadata, or nested structures in your JSON response.
                        Do NOT use a different format or structure for your response.
                        The response must be a valid JSON object that can be directly parsed by System.Text.Json.JsonSerializer.
                        
                        Example of a correct JSON response:
                        {
                          "FlashCards": [
                            {
                              "Front": "hello",
                              "Back": "привет",
                              "IsReversed": true
                            },
                            {
                              "Front": "What is the capital of France?",
                              "Back": "Paris",
                              "IsReversed": false
                            }
                          ]
                        }
                        """;
        
        
        _jsonSchema = new BinaryData("""
                                     {
                                       "$schema": "http://json-schema.org/draft-07/schema#",
                                       "title": "FlashCardsResponse",
                                       "type": "object",
                                       "properties": {
                                         "FlashCards": {
                                           "type": "array",
                                           "items": {
                                             "type": "object",
                                             "properties": {
                                               "Front": {
                                                 "type": "string",
                                                 "description": "The front side of the flashcard"
                                               },
                                               "Back": {
                                                 "type": "string",
                                                 "description": "The back side of the flashcard"
                                               },
                                               "IsReversed": {
                                                 "type": "boolean",
                                                 "description": "Indicates if the card is reversed"
                                               }
                                             },
                                             "required": ["Front", "Back", "IsReversed"]
                                           }
                                         }
                                       },
                                       "required": ["FlashCards"]
                                     }
                                     """);
    }

    public override string Introduction { get; }
    
    public override IAgent CreateOpenAiAgent(ChatClient client)
    {
        return new OpenAIChatAgent(
            chatClient: client,
            name: _name,
            temperature: Temperature,
            systemMessage: _instruction,
            responseFormat: ChatResponseFormat.CreateJsonSchemaFormat("FlashCard", _jsonSchema))
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