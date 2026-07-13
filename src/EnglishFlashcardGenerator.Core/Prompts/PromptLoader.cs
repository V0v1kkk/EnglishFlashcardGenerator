using System;
using System.IO;
using System.Reflection;
using System.Text;

namespace EnglishFlashcardGenerator.Core.Prompts;

public static class PromptLoader
{
    public static string GetPrompt(string promptName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = $"EnglishFlashcardGenerator.Core.Prompts.{promptName}.md";

        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null)
        {
            throw new ArgumentException($"Prompt resource '{resourceName}' not found in assembly.");
        }

        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
