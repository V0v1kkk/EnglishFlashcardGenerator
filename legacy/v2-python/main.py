"""
English Flashcard Generator - Python AG2 Implementation

This program reads a markdown file containing English learning notes,
processes them using a group of specialized AI agents, and generates
flashcards in a structured format.
"""

import os
import logging
import json
import asyncio
from datetime import datetime
from typing import Dict, Any, List, Optional

# Import AG2 (pyautogen)
import autogen

# Import local modules
from config.config_loader import AppSettings, AgentModelSettings
from note_tools import parse_date
from data_classes import FlashCard, FlashCardsResponse
from agents.agent_base import AgentBase
from agents.english_teacher_agent import EnglishTeacherAgent
from agents.flashcard_reviewer_agent import FlashcardReviewerAgent
from agents.flashcard_extractor_agent import FlashCardExtractorAgent


# Constants
MAX_PROCESSING_ATTEMPTS = 3
# For testing purposes, set to True to process only the first section
TEST_MODE = False
MAX_SECTIONS_IN_TEST_MODE = 1


def create_agent_for_agent(agent_name: str, agent_base: AgentBase, app_settings: AppSettings):
    """
    Create an agent with the appropriate configuration.
    
    Args:
        agent_name: The name of the agent.
        agent_base: The base agent.
        app_settings: The application settings.
        
    Returns:
        The created agent.
    """
    # Get the agent-specific settings
    agent_model_settings = None
    if agent_name == "TeacherAgent":
        agent_model_settings = app_settings.agents.teacher_agent
    elif agent_name == "ReviewerAgent":
        agent_model_settings = app_settings.agents.reviewer_agent
    elif agent_name == "ExtractorAgent":
        agent_model_settings = app_settings.agents.extractor_agent
    
    if agent_model_settings is None:
        logging.warning(f"No specific configuration found for agent {agent_name}, using default provider")
        
        # Use the first provider as default if available
        if len(app_settings.providers) > 0:
            return create_agent_with_provider(agent_base, app_settings.providers[0], agent_model_settings or AgentModelSettings())
        
        raise ValueError("No providers configured")
    
    # Find the provider by name
    provider_settings = app_settings.get_provider_by_name(agent_model_settings.provider_name)
    if provider_settings is None:
        logging.error(f"Provider not found: {agent_model_settings.provider_name} for agent: {agent_name}")
        raise ValueError(f"Provider not found: {agent_model_settings.provider_name}")
    
    logging.info(f"Creating agent {agent_name} using provider: {provider_settings.name}")
    
    return create_agent_with_provider(agent_base, provider_settings, agent_model_settings)


def create_agent_with_provider(agent_base: AgentBase, provider_settings, agent_settings):
    """
    Create an agent with the specified provider.
    
    Args:
        agent_base: The base agent.
        provider_settings: The provider settings.
        agent_settings: The agent settings.
        
    Returns:
        The created agent.
    """
    if not provider_settings.type:
        logging.error(f"Provider type is not specified for provider: {provider_settings.name}")
        raise ValueError(f"Provider type is not specified for provider: {provider_settings.name}")
    
    logging.info(f"Creating agent with provider: {provider_settings.name} of type: {provider_settings.type}")
    
    # Use the appropriate agent creation method based on provider type
    if provider_settings.type == "OpenAI":
        return create_agent_with_openai(agent_base, provider_settings.openai, agent_settings)
    elif provider_settings.type == "Azure":
        return create_agent_with_azure(agent_base, provider_settings.azure, agent_settings)
    elif provider_settings.type == "OpenRouter":
        return create_agent_with_openrouter(agent_base, provider_settings.openrouter, agent_settings)
    else:
        raise ValueError(f"Unknown provider type: {provider_settings.type}")


def create_agent_with_openai(agent_base: AgentBase, settings, agent_settings):
    """
    Create an agent using OpenAI configuration.
    
    Args:
        agent_base: The base agent.
        settings: The OpenAI settings.
        agent_settings: The agent settings.
        
    Returns:
        The created agent.
    """
    if settings is None:
        logging.error("OpenAI settings are not configured")
        raise ValueError("OpenAI settings are not configured")
    
    if not settings.api_key:
        logging.error("OpenAI API key is not configured")
        raise ValueError("OpenAI API key is not configured")
    
    logging.info(f"Creating OpenAI agent with model: {settings.model_name}, temperature: {agent_settings.temperature}")
    
    # Create OpenAI configuration
    config = {
        "api_key": settings.api_key,
        "model": settings.model_name
    }
    
    # Add temperature only if the model supports it
    if settings.use_temperature:
        config["temperature"] = agent_settings.temperature
    
    # Use the create_openai_agent method
    return agent_base.create_openai_agent(config)


def create_agent_with_azure(agent_base: AgentBase, settings, agent_settings):
    """
    Create an agent using Azure configuration.
    
    Args:
        agent_base: The base agent.
        settings: The Azure settings.
        agent_settings: The agent settings.
        
    Returns:
        The created agent.
    """
    if settings is None:
        logging.error("Azure settings are not configured")
        raise ValueError("Azure settings are not configured")
    
    if not settings.api_key:
        logging.error("Azure API key is not configured")
        raise ValueError("Azure API key is not configured")
    
    if not settings.endpoint:
        logging.error("Azure endpoint is not configured")
        raise ValueError("Azure endpoint is not configured")
    
    logging.info(f"Creating Azure agent with model: {settings.model_name}, temperature: {agent_settings.temperature}")
    
    # Create Azure configuration
    config = {
        "api_key": settings.api_key,
        "base_url": settings.endpoint,
        "api_version": settings.api_version or "2024-08-01-preview",
        "api_type": "azure"
    }
    
    # Add temperature only if the model supports it
    if settings.use_temperature:
        config["temperature"] = agent_settings.temperature
    
    # Use the create_azure_agent method
    return agent_base.create_azure_agent(config, settings.model_name)


def create_agent_with_openrouter(agent_base: AgentBase, settings, agent_settings):
    """
    Create an agent using OpenRouter configuration.
    
    Args:
        agent_base: The base agent.
        settings: The OpenRouter settings.
        agent_settings: The agent settings.
        
    Returns:
        The created agent.
    """
    if settings is None:
        logging.error("OpenRouter settings are not configured")
        raise ValueError("OpenRouter settings are not configured")
    
    if not settings.api_key:
        logging.error("OpenRouter API key is not configured")
        raise ValueError("OpenRouter API key is not configured")
    
    logging.info(f"Creating OpenRouter agent with model: {settings.model_name}, temperature: {agent_settings.temperature}")
    
    # Create OpenRouter configuration (similar to OpenAI but with endpoint)
    config = {
        "api_key": settings.api_key,
        "model": settings.model_name,
        "base_url": settings.endpoint
    }
    
    # Add temperature only if the model supports it
    if settings.use_temperature:
        config["temperature"] = agent_settings.temperature
    
    # Use the create_openai_agent method (OpenRouter uses OpenAI-compatible API)
    return agent_base.create_openai_agent(config)


def custom_speaker_selection(last_speaker, groupchat):
    """
    Custom speaker selection function for the group chat.
    
    Args:
        last_speaker: The last speaker in the group chat.
        groupchat: The group chat.
        
    Returns:
        The next speaker.
    """
    messages = groupchat.messages
    
    # Get the agents by name
    user_proxy = None
    teacher_agent = None
    reviewer_agent = None
    extractor_agent = None
    
    for agent in groupchat.agents:
        if agent.name == "UserProxy":
            user_proxy = agent
        elif agent.name == "EnglishTeacherAgent":
            teacher_agent = agent
        elif agent.name == "FlashcardReviewerAgent":
            reviewer_agent = agent
        elif agent.name == "FlashCardExtractorAgent":
            extractor_agent = agent
    
    # Initial message from user proxy goes to teacher agent
    if len(messages) <= 1:
        return teacher_agent
    
    # Check for approval in the reviewer's message
    if last_speaker is reviewer_agent:
        if "OK!" in messages[-1]["content"]:
            # If the reviewer approves, move to the extractor
            return extractor_agent
        else:
            # If the reviewer has suggestions, go back to the teacher
            return teacher_agent
    
    # Teacher agent's response goes to reviewer
    if last_speaker is teacher_agent:
        return reviewer_agent
    
    # Extractor agent's response goes back to user proxy
    # After the extractor agent has processed the cards, we want to terminate the conversation
    if last_speaker is extractor_agent:
        # Return None to terminate the conversation
        return None
    
    # Default to random selection if we can't determine the next speaker
    return "random"


async def process_section_with_groupchat(section: str, app_settings: AppSettings) -> Optional[str]:
    """
    Process a section using a group chat with a finite state machine.
    
    Args:
        section: The section to process.
        app_settings: The application settings.
        
    Returns:
        The formatted flashcards, or None if processing failed.
    """
    try:
        # Create the agents
        teacher_settings = app_settings.agents.teacher_agent
        teacher = EnglishTeacherAgent(teacher_settings.temperature, teacher_settings.max_tokens)
        teacher_agent = create_agent_for_agent("TeacherAgent", teacher, app_settings)
        
        reviewer_settings = app_settings.agents.reviewer_agent
        reviewer = FlashcardReviewerAgent(reviewer_settings.temperature, reviewer_settings.max_tokens)
        reviewer_agent = create_agent_for_agent("ReviewerAgent", reviewer, app_settings)
        
        extractor_settings = app_settings.agents.extractor_agent
        extractor = FlashCardExtractorAgent(extractor_settings.temperature, extractor_settings.max_tokens)
        extractor_agent = create_agent_for_agent("ExtractorAgent", extractor, app_settings)
        
        # Create a user proxy agent with TERMINATE mode
        user_proxy = autogen.UserProxyAgent(
            name="UserProxy",
            human_input_mode="TERMINATE",
            is_termination_msg=lambda x: x.get("name") == "FlashCardExtractorAgent" and "FlashCards" in x.get("content", ""),
            code_execution_config=False  # Disable code execution
        )
        
        # Define the allowed transitions between agents
        allowed_transitions = {
            user_proxy: [teacher_agent],
            teacher_agent: [reviewer_agent],
            reviewer_agent: [teacher_agent, extractor_agent],
            extractor_agent: [user_proxy]
        }
        
        # Create the group chat
        group_chat = autogen.GroupChat(
            agents=[user_proxy, teacher_agent, reviewer_agent, extractor_agent],
            messages=[],
            max_round=15,
            speaker_selection_method=custom_speaker_selection,
            allowed_or_disallowed_speaker_transitions=allowed_transitions,
            speaker_transitions_type="allowed"
        )
        
        # Get the provider settings for the LLM config
        provider_settings = app_settings.get_provider_by_name(teacher_settings.provider_name)
        
        # Create LLM config for the manager
        if provider_settings.type == "OpenAI":
            llm_config = {
                "api_key": provider_settings.openai.api_key,
                "model": provider_settings.openai.model_name
            }
            
            # Add temperature only if the model supports it
            if provider_settings.openai.use_temperature:
                llm_config["temperature"] = 0.7
                
        elif provider_settings.type == "Azure":
            llm_config = {
                "api_key": provider_settings.azure.api_key,
                "endpoint": provider_settings.azure.endpoint,
                "api_version": provider_settings.azure.api_version or "2024-08-01-preview",
                "model": provider_settings.azure.model_name
            }
            
            # Add temperature only if the model supports it
            if provider_settings.azure.use_temperature:
                llm_config["temperature"] = 0.7
                
        else:
            # Default to OpenAI if provider type is unknown
            llm_config = {
                "api_key": provider_settings.openai.api_key,
                "model": provider_settings.openai.model_name
            }
            
            # Add temperature only if the model supports it
            if provider_settings.openai.use_temperature:
                llm_config["temperature"] = 0.7
        
        # Create the manager
        manager = autogen.GroupChatManager(
            groupchat=group_chat,
            llm_config=llm_config
        )
        
        # Initiate the chat
        message = f"""
        Extract cards from the note:
        {section}
        """
        
        chat_result = user_proxy.initiate_chat(
            recipient=manager,
            message=message
        )
        
        # Extract the last message from the extractor agent
        extractor_response = None
        for msg in reversed(chat_result.chat_history):
            if msg.get("name") == extractor_agent.name:
                extractor_response = msg.get("content")
                break
        
        if not extractor_response:
            logging.error("No response from extractor agent")
            return None
        
        # Clean up the content to ensure it's valid JSON
        content = extractor_response.strip()
        if content.startswith("```json"):
            content = content[7:]
        if content.startswith("```"):
            content = content[3:]
        if content.endswith("```"):
            content = content[:-3]
        content = content.strip()
        
        # Parse the JSON
        flash_cards_dict = json.loads(content)
        
        # Convert capitalized field names to lowercase
        if "FlashCards" in flash_cards_dict:
            for i, card in enumerate(flash_cards_dict["FlashCards"]):
                if "Front" in card:
                    card["front"] = card.pop("Front")
                if "Back" in card:
                    card["back"] = card.pop("Back")
                if "IsReversed" in card:
                    card["is_reversed"] = card.pop("IsReversed")
        
        # Parse the modified JSON
        flash_cards_response = FlashCardsResponse(**flash_cards_dict)
        
        # Return the formatted flashcards
        return flash_cards_response.format_flash_cards()
    
    except Exception as ex:
        logging.error(f"Error processing section with group chat: {ex}")
        return None


async def process_section_async(section: str, app_settings: AppSettings, section_index: int) -> Optional[str]:
    """
    Process a section of the markdown file.
    
    Args:
        section: The section to process.
        app_settings: The application settings.
        section_index: The index of the section.
        
    Returns:
        The formatted flashcards, or None if processing failed.
    """
    for attempt in range(1, MAX_PROCESSING_ATTEMPTS + 1):
        try:
            logging.info(f"Processing section {section_index}, attempt {attempt}/{MAX_PROCESSING_ATTEMPTS}")
            
            # Process the section using the group chat
            formatted_cards = await process_section_with_groupchat(section, app_settings)
            
            if not formatted_cards:
                logging.warning(f"No content received from the group chat on attempt {attempt}")
                continue
            
            # If we get here, processing was successful
            logging.info(f"Successfully processed section {section_index} on attempt {attempt}")
            return formatted_cards
        
        except Exception as ex:
            logging.error(f"Unexpected error processing section {section_index} on attempt {attempt}/{MAX_PROCESSING_ATTEMPTS}: {ex}")
            
            # If this is the last attempt, log an error
            if attempt == MAX_PROCESSING_ATTEMPTS:
                logging.error(f"Failed to process section {section_index} after {MAX_PROCESSING_ATTEMPTS} attempts")
    
    # If we get here, all attempts failed
    return None


def save_output_files(
    formatted_cards: str,
    note_date_without_day_of_week: str,
    note_date_str: str,
    section_lines: List[str],
    result_cards_folder_path: str,
    result_notes_folder_path: str,
    cards_template: str,
    note_template: str
):
    """
    Save the output files.
    
    Args:
        formatted_cards: The formatted flashcards.
        note_date_without_day_of_week: The note date without the day of the week.
        note_date_str: The note date with the day of the week.
        section_lines: The lines of the section.
        result_cards_folder_path: The path to the result cards folder.
        result_notes_folder_path: The path to the result notes folder.
        cards_template: The cards template.
        note_template: The note template.
    """
    current_timestamp = datetime.now().strftime("%Y-%m-%dT%H:%M:%S")
    
    cards_name = f"EnglishFlashcards-{note_date_without_day_of_week}"
    cards_file_name = f"{cards_name}.md"
    note_name = f"EnglishLearningNote-{note_date_without_day_of_week}"
    
    cards_file_path = os.path.join(result_cards_folder_path, cards_file_name)
    
    formatted_cards_template = cards_template.format(
        current_timestamp,
        note_name,
        note_date_without_day_of_week,
        formatted_cards
    )
    
    # Ensure the directory exists
    os.makedirs(result_cards_folder_path, exist_ok=True)
    
    with open(cards_file_path, 'w') as f:
        f.write(formatted_cards_template)
    
    logging.info(f"Saved cards to {cards_file_path}")
    
    note_file_name = f"{note_name}.md"
    note_file_path = os.path.join(result_notes_folder_path, note_file_name)
    note_text = "\n".join(section_lines[1:])
    
    formatted_note_template = note_template.format(
        current_timestamp,
        cards_name,
        note_date_str,
        note_text
    )
    
    # Ensure the directory exists
    os.makedirs(result_notes_folder_path, exist_ok=True)
    
    with open(note_file_path, 'w') as f:
        f.write(formatted_note_template)
    
    logging.info(f"Saved note to {note_file_path}")


def main():
    """
    Main function.
    """
    # Initialize configuration
    app_settings = AppSettings.load_from_configuration("appsettings.json")
    
    # Initialize logging
    logging.basicConfig(
        level=logging.INFO,
        format='%(asctime)s - %(name)s - %(levelname)s - %(message)s'
    )
    
    try:
        logging.info("Starting English Flashcard Generator")
        
        # Validate that we have at least one provider configured
        if len(app_settings.providers) == 0:
            logging.fatal("No providers configured in appsettings.json")
            return
        
        # Get file paths from configuration
        file_path = app_settings.file_paths.source_note_path
        result_cards_folder_path = app_settings.file_paths.result_cards_folder_path
        result_notes_folder_path = app_settings.file_paths.result_notes_folder_path
        
        logging.info(f"Source note path: {file_path}")
        logging.info(f"Result cards folder path: {result_cards_folder_path}")
        logging.info(f"Result notes folder path: {result_notes_folder_path}")
        
        # Load templates from file system
        card_template_path = os.path.join(os.getcwd(), app_settings.file_paths.card_template_path)
        note_template_path = os.path.join(os.getcwd(), app_settings.file_paths.note_template_path)
        
        logging.info(f"Card template path: {card_template_path}")
        logging.info(f"Note template path: {note_template_path}")
        
        with open(card_template_path, 'r') as f:
            cards_template = f.read()
        
        with open(note_template_path, 'r') as f:
            note_template = f.read()
        
        # Read the source file
        with open(file_path, 'r') as f:
            file_content = f.read()
        
        # Split markdown document into sections by second level headers
        sections = file_content.split("\n## ")[2:]
        sections.reverse()
        
        # In test mode, limit the number of sections to process
        if TEST_MODE:
            logging.info(f"Running in TEST MODE - processing only {MAX_SECTIONS_IN_TEST_MODE} section(s)")
            sections = sections[:MAX_SECTIONS_IN_TEST_MODE]
        
        cards = []
        for index, section in enumerate(sections):
            section_lines = section.split("\n")
            if len(section_lines) < 2:
                logging.error(f"Section {index} has less than 2 lines, skipping")
                continue
            
            first_line = section_lines[0]
            note_date = parse_date(first_line)
            note_date_without_day_of_week = note_date.strftime("%Y-%m-%d")
            note_date_str = note_date.strftime("%Y-%m-%d-%A")
            
            # Process the section and get formatted cards
            formatted_cards = asyncio.run(process_section_async(section, app_settings, index))
            if not formatted_cards:
                logging.error(f"Failed to process section {index}, skipping")
                continue
            
            # Add to list and save into files
            cards.append(formatted_cards)
            save_output_files(
                formatted_cards,
                note_date_without_day_of_week,
                note_date_str,
                section_lines,
                result_cards_folder_path,
                result_notes_folder_path,
                cards_template,
                note_template
            )
        
        logging.info("English Flashcard Generator completed successfully")
    
    except Exception as ex:
        logging.exception("An unhandled exception occurred")


if __name__ == "__main__":
    main()
