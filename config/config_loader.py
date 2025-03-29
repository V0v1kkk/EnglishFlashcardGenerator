import json
import os
from typing import Dict, List, Any, Optional


class FilePathSettings:
    """
    Settings for file paths used in the application.
    """
    def __init__(self):
        self.source_note_path = ""
        self.result_cards_folder_path = ""
        self.result_notes_folder_path = ""
        self.card_template_path = ""
        self.note_template_path = ""
    
    @staticmethod
    def from_dict(data: Dict[str, Any]) -> 'FilePathSettings':
        settings = FilePathSettings()
        settings.source_note_path = data.get("SourceNotePath", "")
        settings.result_cards_folder_path = data.get("ResultCardsFolderPath", "")
        settings.result_notes_folder_path = data.get("ResultNotesFolderPath", "")
        settings.card_template_path = data.get("CardTemplatePath", "")
        settings.note_template_path = data.get("NoteTemplatePath", "")
        return settings


class OpenAISettings:
    """
    Settings for OpenAI provider.
    """
    def __init__(self):
        self.api_key = ""
        self.model_name = "gpt-4o-2024-08-06"
        self.use_temperature = True
        self.use_completion_tokens = False
    
    @staticmethod
    def from_dict(data: Dict[str, Any]) -> 'OpenAISettings':
        settings = OpenAISettings()
        settings.api_key = data.get("ApiKey", "")
        settings.model_name = data.get("ModelName", "gpt-4o-2024-08-06")
        settings.use_temperature = data.get("UseTemperature", True)
        settings.use_completion_tokens = data.get("UseCompletionTokens", False)
        return settings


class AzureSettings:
    """
    Settings for Azure OpenAI provider.
    """
    def __init__(self):
        self.api_key = ""
        self.endpoint = ""
        self.model_name = ""
        self.api_version = None
        self.use_temperature = True
        self.use_completion_tokens = False
    
    @staticmethod
    def from_dict(data: Dict[str, Any]) -> 'AzureSettings':
        settings = AzureSettings()
        settings.api_key = data.get("ApiKey", "")
        settings.endpoint = data.get("Endpoint", "")
        settings.model_name = data.get("ModelName", "")
        settings.api_version = data.get("ApiVersion", None)
        settings.use_temperature = data.get("UseTemperature", True)
        settings.use_completion_tokens = data.get("UseCompletionTokens", False)
        return settings


class OpenRouterSettings:
    """
    Settings for OpenRouter provider.
    """
    def __init__(self):
        self.api_key = ""
        self.endpoint = "https://openrouter.ai/api/"
        self.model_name = "openai/o1-mini"
        self.use_temperature = True
        self.use_completion_tokens = False
    
    @staticmethod
    def from_dict(data: Dict[str, Any]) -> 'OpenRouterSettings':
        settings = OpenRouterSettings()
        settings.api_key = data.get("ApiKey", "")
        settings.endpoint = data.get("Endpoint", "https://openrouter.ai/api/")
        settings.model_name = data.get("ModelName", "openai/o1-mini")
        settings.use_temperature = data.get("UseTemperature", True)
        settings.use_completion_tokens = data.get("UseCompletionTokens", False)
        return settings


class ProviderSettings:
    """
    Settings for a provider.
    """
    def __init__(self):
        self.name = ""
        self.type = ""
        self.openai = None
        self.azure = None
        self.openrouter = None
    
    @staticmethod
    def from_dict(data: Dict[str, Any]) -> 'ProviderSettings':
        settings = ProviderSettings()
        settings.name = data.get("Name", "")
        settings.type = data.get("Type", "")
        
        if "OpenAI" in data and data["OpenAI"]:
            settings.openai = OpenAISettings.from_dict(data["OpenAI"])
        
        if "Azure" in data and data["Azure"]:
            settings.azure = AzureSettings.from_dict(data["Azure"])
        
        if "OpenRouter" in data and data["OpenRouter"]:
            settings.openrouter = OpenRouterSettings.from_dict(data["OpenRouter"])
        
        return settings
    
    def get_settings(self):
        """
        Get the appropriate settings based on the provider type.
        """
        if self.type == "OpenAI":
            return self.openai
        elif self.type == "Azure":
            return self.azure
        elif self.type == "OpenRouter":
            return self.openrouter
        else:
            return None


class AgentModelSettings:
    """
    Settings for an agent model.
    """
    def __init__(self):
        self.provider_name = "Default"
        self.temperature = 0.7
        self.max_tokens = 16384
    
    @staticmethod
    def from_dict(data: Dict[str, Any]) -> 'AgentModelSettings':
        settings = AgentModelSettings()
        settings.provider_name = data.get("ProviderName", "Default")
        settings.temperature = data.get("Temperature", 0.7)
        settings.max_tokens = data.get("MaxTokens", 16384)
        return settings


class AgentSettings:
    """
    Settings for all agents.
    """
    def __init__(self):
        self.teacher_agent = AgentModelSettings()
        self.reviewer_agent = AgentModelSettings()
        self.extractor_agent = AgentModelSettings()
    
    @staticmethod
    def from_dict(data: Dict[str, Any]) -> 'AgentSettings':
        settings = AgentSettings()
        
        if "TeacherAgent" in data:
            settings.teacher_agent = AgentModelSettings.from_dict(data["TeacherAgent"])
        
        if "ReviewerAgent" in data:
            settings.reviewer_agent = AgentModelSettings.from_dict(data["ReviewerAgent"])
        
        if "ExtractorAgent" in data:
            settings.extractor_agent = AgentModelSettings.from_dict(data["ExtractorAgent"])
        
        return settings


class AppSettings:
    """
    Application settings.
    """
    def __init__(self):
        self.file_paths = FilePathSettings()
        self.providers = []
        self.agents = AgentSettings()
    
    @staticmethod
    def load_from_configuration(config_path: str) -> 'AppSettings':
        settings = AppSettings()
        
        with open(config_path, 'r') as f:
            config = json.load(f)
        
        # Bind the FilePaths section
        if 'FilePaths' in config:
            settings.file_paths = FilePathSettings.from_dict(config['FilePaths'])
        
        # Bind the Providers section
        if 'Providers' in config:
            for provider in config['Providers']:
                settings.providers.append(ProviderSettings.from_dict(provider))
        
        # Bind the Agents section
        if 'Agents' in config:
            settings.agents = AgentSettings.from_dict(config['Agents'])
        
        return settings
    
    def get_provider_by_name(self, provider_name: str) -> Optional[ProviderSettings]:
        """
        Find a provider by name.
        """
        for provider in self.providers:
            if provider.name == provider_name:
                return provider
        return None