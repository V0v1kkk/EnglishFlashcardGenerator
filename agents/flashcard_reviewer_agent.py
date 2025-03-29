import autogen
from .agent_base import AgentBase
from typing import Dict, Any


class FlashcardReviewerAgent(AgentBase):
    """
    Agent that reviews flashcards for quality and correctness.
    """
    def __init__(self, temperature: float = 1.0, max_tokens: int = 16384):
        super().__init__(temperature, max_tokens)
        self._name = "FlashcardReviewerAgent"
        
        self._instruction = """
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
        """
    
    @property
    def introduction(self) -> str:
        return "I am an English learning methodical expert. I will review the flashcards and provide feedback."
    
    def create_openai_agent(self, config: Dict[str, Any]):
        self._agent = autogen.AssistantAgent(
            name=self._name,
            system_message=self._instruction,
            llm_config=config
        )
        return self._agent
    
    def create_azure_agent(self, config: Dict[str, Any], model_name: str):
        # Configure for Azure
        azure_config = config.copy()
        azure_config["model"] = model_name
        
        self._agent = autogen.AssistantAgent(
            name=self._name,
            system_message=self._instruction,
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