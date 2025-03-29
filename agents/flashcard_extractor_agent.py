import autogen
from .agent_base import AgentBase
from typing import Dict, Any
from pydantic import BaseModel, Field
from typing import List


class FlashCard(BaseModel):
    """
    Represents a single flashcard with front, back, and is_reversed properties.
    """
    front: str
    back: str
    is_reversed: bool = Field(default=False)


class FlashCardsResponse(BaseModel):
    """
    Contains a list of flashcards.
    """
    flash_cards: List[FlashCard] = Field(alias="FlashCards")


class FlashCardExtractorAgent(AgentBase):
    """
    Agent that formats flashcards into JSON.
    """
    def __init__(self, temperature: float = 0.2, max_tokens: int = 16384):
        super().__init__(temperature, max_tokens)
        self._name = "FlashCardExtractorAgent"
        
        self._instruction = """
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
        
        IMPORTANT: Your entire response should be ONLY the JSON object, with no additional text, explanations, or code blocks.
        Do NOT wrap your JSON in markdown code blocks or any other formatting.
        
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
        """
    
    @property
    def introduction(self) -> str:
        return """
        I will format questions and answers into flashcards.
        I can make flashcard from question and answer pair.
        And also can make reversed flashcard where answer will be a question as well.
        Multiline flashcards are also supported.
        Tell me what you need and I will generate flashcards for you.
        """
    
    def create_openai_agent(self, config: Dict[str, Any]):
        # Check if the model supports response_format
        model_name = config.get("model", "")
        
        # Models that don't support response_format
        unsupported_models = ["o3-mini-2025-01-31"]
        
        # Add response format to config if supported
        config_with_format = config.copy()
        if model_name not in unsupported_models:
            config_with_format["response_format"] = {"type": "json_object"}
        
        # Add a stronger instruction for models that don't support response_format
        instruction = self._instruction
        if model_name in unsupported_models:
            instruction += """
            
            IMPORTANT: Since you don't support the response_format parameter, it's crucial that you 
            strictly follow the JSON format instructions above. Your entire response must be a valid 
            JSON object and nothing else - no explanations, no markdown formatting, just the JSON.
            """
        
        self._agent = autogen.AssistantAgent(
            name=self._name,
            system_message=instruction,
            llm_config=config_with_format
        )
        return self._agent
    
    def create_azure_agent(self, config: Dict[str, Any], model_name: str):
        # Configure for Azure
        azure_config = config.copy()
        azure_config["model"] = model_name
        
        # Models that don't support response_format
        unsupported_models = ["o3-mini-2025-01-31"]
        
        # Add response format to config if supported
        if model_name not in unsupported_models:
            azure_config["response_format"] = {"type": "json_object"}
        
        # Add a stronger instruction for models that don't support response_format
        instruction = self._instruction
        if model_name in unsupported_models:
            instruction += """
            
            IMPORTANT: Since you don't support the response_format parameter, it's crucial that you 
            strictly follow the JSON format instructions above. Your entire response must be a valid 
            JSON object and nothing else - no explanations, no markdown formatting, just the JSON.
            """
        
        self._agent = autogen.AssistantAgent(
            name=self._name,
            system_message=instruction,
            llm_config=azure_config
        )
        return self._agent
    
    async def generate_async(self, message: str) -> str:
        """
        Generate a response asynchronously.
        
        Args:
            message: The message to generate a response for.
            
        Returns:
            The generated response.
        """
        if not hasattr(self, '_agent'):
            raise ValueError("Agent not initialized. Call create_openai_agent or create_azure_agent first.")
        
        # Create a user proxy agent to initiate the chat
        user_proxy = autogen.UserProxyAgent(
            name="UserProxy",
            human_input_mode="NEVER",
            code_execution_config=False  # Disable code execution
        )
        
        # Send a message to the agent
        await user_proxy.a_send(
            message=message,
            recipient=self._agent
        )
        
        # Get the reply from the agent
        reply = await self._agent.a_generate_reply(sender=user_proxy)
        
        return reply or ""