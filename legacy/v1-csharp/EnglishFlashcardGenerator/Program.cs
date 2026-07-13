using System.ClientModel;
using System.Reflection;
using AutoGen;
using AutoGen.Core;
using Azure.AI.OpenAI;
using Microsoft.Extensions.Configuration;
using OpenAI.Chat;
using Serilog;
using System.Text.Json;
using EnglishFlashcardGenerator.Configuration;

namespace EnglishFlashcardGenerator;

static class Program
{
    private const int MaxProcessingAttempts = 3;
    
    static async Task Main(string[] args)
    {
        // Initialize configuration
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .AddEnvironmentVariables()
            .Build();

        // Initialize Serilog
        Log.Logger = new LoggerConfiguration()
            .ReadFrom.Configuration(configuration)
            .Enrich.WithProperty("Application", "FlashcardGenerator")
            .Enrich.WithProperty("RunId", Guid.NewGuid())
            .CreateLogger();

        try
        {
            Log.Information("Starting English Flashcard Generator");

            // Load application settings
            var appSettings = AppSettings.LoadFromConfiguration(configuration);
            
            // Validate that we have at least one provider configured
            if (appSettings.Providers.Count == 0)
            {
                Log.Fatal("No providers configured in appsettings.json");
                return;
            }
            
            // Get file paths from configuration
            var filePath = appSettings.FilePaths.SourceNotePath;
            var resultCardsFolderPath = appSettings.FilePaths.ResultCardsFolderPath;
            var resultNotesFolderPath = appSettings.FilePaths.ResultNotesFolderPath;
            
            Log.Information("Source note path: {FilePath}", filePath);
            Log.Information("Result cards folder path: {ResultCardsFolderPath}", resultCardsFolderPath);
            Log.Information("Result notes folder path: {ResultNotesFolderPath}", resultNotesFolderPath);
            
            // Load templates from file system
            var cardTemplatePath = Path.Combine(Directory.GetCurrentDirectory(), appSettings.FilePaths.CardTemplatePath);
            var noteTemplatePath = Path.Combine(Directory.GetCurrentDirectory(), appSettings.FilePaths.NoteTemplatePath);
            
            Log.Information("Card template path: {CardTemplatePath}", cardTemplatePath);
            Log.Information("Note template path: {NoteTemplatePath}", noteTemplatePath);
            
            var cardsTemplate = await File.ReadAllTextAsync(cardTemplatePath);
            var noteTemplate = await File.ReadAllTextAsync(noteTemplatePath);
            
            // Read the source file
            var fileContent = await File.ReadAllTextAsync(filePath);
            
            // Split markdown document into sections by second level headers ('## ')
            // Split only by lines that start with '## '
            var sections = fileContent.Split(["\n## "], StringSplitOptions.RemoveEmptyEntries)
                .Skip(2)
                .Reverse()
                .ToList();
            
            // For tests
            //sections = sections.Skip(179).ToList();
            
            var cards = new List<string>();
            for (var index = 0; index < sections.Count; index++)
            {
                var section = sections[index];
                var sectionLines = section.Split("\n");
                if (sectionLines.Length < 2)
                {
                    Log.Error("Section {Index} has less than 2 lines, skipping", index);
                    continue;
                }
                
                var theFirstLine = sectionLines.First();
                var noteDate = NoteTools.ParseDate(theFirstLine);
                var noteDateWithoutDayOfWeek = noteDate.ToString("yyyy-MM-dd");
                var noteDateStr = noteDate.ToString("yyyy-MM-dd-dddd");

                // Process the section and get formatted cards
                var formattedCards = await ProcessSectionAsync(section, appSettings, index);
                if (string.IsNullOrWhiteSpace(formattedCards))
                {
                    Log.Error("Failed to process section {Index}, skipping", index);
                    continue;
                }

                // Add to list and save into files
                cards.Add(formattedCards);
                await SaveOutputFilesAsync(
                    formattedCards, 
                    noteDateWithoutDayOfWeek, 
                    noteDateStr, 
                    sectionLines, 
                    resultCardsFolderPath, 
                    resultNotesFolderPath, 
                    cardsTemplate, 
                    noteTemplate);
            }
            
            Log.Information("English Flashcard Generator completed successfully");
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "An unhandled exception occurred");
        }
        finally
        {
            await Log.CloseAndFlushAsync();
        }
    }

    private static async Task<string> ProcessSectionAsync(string section, AppSettings appSettings, int sectionIndex)
    {
        for (int attempt = 1; attempt <= MaxProcessingAttempts; attempt++)
        {
            try
            {
                Log.Information("Processing section {Index}, attempt {Attempt}/{MaxAttempts}", 
                    sectionIndex, attempt, MaxProcessingAttempts);
                
                // Get content from the agent
                var content = await GetContentFromAgentAsync(section, appSettings);
                
                if (string.IsNullOrWhiteSpace(content))
                {
                    Log.Warning("No content received from the agent on attempt {Attempt}", attempt);
                    continue;
                }
                
                // Try to deserialize the content
                var flashCards = content.DeserializeFlashCards();
                
                // If we get here, deserialization was successful
                Log.Information("Successfully processed section {Index} on attempt {Attempt}", sectionIndex, attempt);
                return flashCards.FormatFlashCards();
            }
            catch (JsonException ex)
            {
                Log.Warning(ex, "Failed to deserialize JSON on attempt {Attempt}/{MaxAttempts}", 
                    attempt, MaxProcessingAttempts);
                
                // If this is the last attempt, log an error
                if (attempt == MaxProcessingAttempts)
                {
                    Log.Error("Failed to process section {Index} after {MaxAttempts} attempts", 
                        sectionIndex, MaxProcessingAttempts);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Unexpected error processing section {Index} on attempt {Attempt}/{MaxAttempts}", 
                    sectionIndex, attempt, MaxProcessingAttempts);
                
                // If this is the last attempt, log an error
                if (attempt == MaxProcessingAttempts)
                {
                    Log.Error("Failed to process section {Index} after {MaxAttempts} attempts", 
                        sectionIndex, MaxProcessingAttempts);
                }
            }
        }
        
        // If we get here, all attempts failed
        return string.Empty;
    }
    
    private static async Task<string?> GetContentFromAgentAsync(string content, AppSettings appSettings)
    {
        var (theLastAgent, groupChat) = CreateGroupAndTheLastAgent(appSettings);
        
        var message = $"""
                      Extract cards from the note:
                      {content}
                      """;
        
        var taskMessage = new TextMessage(Role.User, content: message, from: theLastAgent.Name);
        string? lastMessageContent = null;
        
        await foreach (var response in groupChat.SendAsync([taskMessage], maxRound: 15))
        {
            // terminate chat if message is from runner and run successfully
            if (response.From == theLastAgent.Name)
            {
                lastMessageContent = response.GetContent();
                break;
            }
        }
        
        return lastMessageContent;
    }
    
    private static async Task SaveOutputFilesAsync(
        string formattedCards, 
        string noteDateWithoutDayOfWeek, 
        string noteDateStr, 
        string[] sectionLines, 
        string resultCardsFolderPath, 
        string resultNotesFolderPath, 
        string cardsTemplate, 
        string noteTemplate)
    {
        var currentTimeStumpString = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss");
        
        var cardsName = $"EnglishFlashcards-{noteDateWithoutDayOfWeek}";
        var cardsFileName = $"{cardsName}.md";
        var noteName = $"EnglishLearningNote-{noteDateWithoutDayOfWeek}";
        
        var cardsFilePath = Path.Combine(resultCardsFolderPath, cardsFileName);
        
        var formattedCardsTemplate = string.Format(cardsTemplate,
            currentTimeStumpString,
            noteName,
            noteDateWithoutDayOfWeek,
            formattedCards);
        await File.WriteAllTextAsync(cardsFilePath, formattedCardsTemplate);
        Log.Information("Saved cards to {CardsFilePath}", cardsFilePath);
        
        var noteFileName = $"{noteName}.md";
        var noteFilePath = Path.Combine(resultNotesFolderPath, noteFileName);
        var noteText = sectionLines.Skip(1).Aggregate((a, b) => $"{a}\n{b}");
        
        var formattedNoteTemplate = string.Format(noteTemplate,
            currentTimeStumpString,
            cardsName,
            noteDateStr,
            noteText);
        await File.WriteAllTextAsync(noteFilePath, formattedNoteTemplate);
        Log.Information("Saved note to {NoteFilePath}", noteFilePath);
    }

    private static (IAgent theLastAgent, GroupChat groupChat) CreateGroupAndTheLastAgent(AppSettings appSettings)
    {
        var userProxyAgent = new UserProxyAgent("UserProxy", humanInputMode: HumanInputMode.NEVER);
        
        // Get agent settings
        var teacherSettings = appSettings.Agents.TeacherAgent;
        var extractorSettings = appSettings.Agents.ExtractorAgent;
        var reviewerSettings = appSettings.Agents.ReviewerAgent;
        
        // Create agent instances with configuration settings
        var teacher = new EnglishTeacherAgent(teacherSettings.Temperature, teacherSettings.MaxTokens);
        var extractor = new FlashCardExtractorAgent(extractorSettings.Temperature, extractorSettings.MaxTokens);
        var flashcardReviewer = new FlashcardReviewerAgent(reviewerSettings.Temperature, reviewerSettings.MaxTokens);
        
        // Create agents with their specific configurations
        var teacherAgent = ChatClientFactory.CreateAgentForAgent(
            "TeacherAgent", 
            teacher,
            appSettings,
            Log.Logger);
        
        var extractorAgent = ChatClientFactory.CreateAgentForAgent(
            "ExtractorAgent", 
            extractor,
            appSettings,
            Log.Logger);
        
        var flashcardReviewerAgent = ChatClientFactory.CreateAgentForAgent(
            "ReviewerAgent", 
            flashcardReviewer,
            appSettings,
            Log.Logger);
        
        // Define transitions between agents
        var userProxy2TeacherTransition = Transition.Create(userProxyAgent, teacherAgent);
        var teacher2ReviewerTransition = Transition.Create(teacherAgent, flashcardReviewerAgent);
        var reviewer2TeacherTransition = Transition.Create(
            from: flashcardReviewerAgent,
            to: teacherAgent, 
            canTransitionAsync: (from, to, messages) =>
            {
                return Task.FromResult(messages.Last() is TextMessage textMessage &&
                                       !textMessage.Content.Split('\n').Last().Contains("OK!"));
            });
        var reviewer2ExtractorTransition = Transition.Create(
            from: flashcardReviewerAgent,
            to: extractorAgent, 
            canTransitionAsync: (from, to, messages) =>
            {
                return Task.FromResult(messages.Last() is TextMessage textMessage &&
                                       textMessage.Content.Split('\n').Last().Contains("OK!"));
            });
        var extractor2AdminTransition = Transition.Create(extractorAgent, userProxyAgent);
        
        var workflow = new Graph(
            [
                userProxy2TeacherTransition,
                teacher2ReviewerTransition,
                reviewer2TeacherTransition,
                reviewer2ExtractorTransition,
                extractor2AdminTransition,
            ]);
        
        var groupChat = new GroupChat(
            workflow: workflow,
            members:
            [
                userProxyAgent,
                teacherAgent,
                extractorAgent,
                flashcardReviewerAgent,
            ]);
        
        return (extractorAgent, groupChat);
    }
}