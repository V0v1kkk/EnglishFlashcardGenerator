from abc import ABC, abstractmethod
from typing import Dict, Any


class AgentBase(ABC):
    """
    Base class for all agents in the system.
    """
    def __init__(self, temperature: float = 0.7, max_tokens: int = 16384):
        self.temperature = temperature
        self.max_tokens = max_tokens
    
    @property
    @abstractmethod
    def introduction(self) -> str:
        """Return the agent's introduction"""
        pass
    
    @abstractmethod
    def create_openai_agent(self, config: Dict[str, Any]):
        """Create an agent using OpenAI configuration"""
        pass
    
    @abstractmethod
    def create_azure_agent(self, config: Dict[str, Any], model_name: str):
        """Create an agent using Azure configuration"""
        pass
    
    async def generate_async(self, message: str) -> str:
        """
        Generate a response asynchronously.
        
        Args:
            message: The message to generate a response for.
            
        Returns:
            The generated response.
        """
        # This is a placeholder method that should be implemented by subclasses
        # In the actual implementation, this would call the appropriate API
        # to generate a response asynchronously
        raise NotImplementedError("Subclasses must implement generate_async")