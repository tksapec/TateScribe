using TateScribe.Core.ChatGpt;

namespace TateScribe.Core.Proofreading;

public static class FinalProofreadingPrompt
{
    public static string Text { get; } =
        new ChatGptPromptTemplateProvider().GetTemplate(ChatGptTaskType.TextProofreading);
}
