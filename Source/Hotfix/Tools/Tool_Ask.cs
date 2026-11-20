using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Hotfix;

public sealed record AskOption(string Label, string? Description);

public sealed record AskQuestion(string Question, IReadOnlyList<AskOption> Options, bool MultiSelect);

public delegate Task<IReadOnlyList<IReadOnlyList<string>>?> AskHandler(IReadOnlyList<AskQuestion> questions, CancellationToken ct);

public sealed class Tool_Ask() : Tool_Base("Ask",
    $"""
    Asks the user up to {MaxQuestions} multiple-choice questions at once and waits for the answers. Each question has {MinOptions} to {MaxOptions} options. The user can always type an answer of their own instead, so do not add an option for that. The user may answer only some of the questions, or close them without answering.
    Use it for a decision that is the user's to make and that you cannot tell from the chat or the game. Put the option you recommend first.
    """,
    $$"""
    {
      "type": "object",
      "properties": {
        "questions": {
          "type": "array",
          "description": "1 to {{MaxQuestions}} questions",
          "items": {
            "type": "object",
            "properties": {
              "question": { "type": "string", "description": "The question, written out in full" },
              "options": {
                "type": "array",
                "description": "{{MinOptions}} to {{MaxOptions}} choices",
                "items": {
                  "type": "object",
                  "properties": {
                    "label": { "type": "string", "description": "The choice in a few words" },
                    "description": { "type": "string", "description": "What choosing it means" }
                  },
                  "required": ["label"]
                }
              },
              "multiSelect": { "type": "boolean", "description": "true lets the user pick more than one option" }
            },
            "required": ["question", "options"]
          }
        }
      },
      "required": ["questions"]
    }
    """)
{
    private const int MaxQuestions = 4;
    private const int MinOptions = 2;
    private const int MaxOptions = 4;

    public override async Task<string> RunAsync(JsonNode arguments, ToolContext context, CancellationToken ct)
    {
        var questions = Parse(arguments);
        if (await context.Ask(questions, ct) is not { } answers)
            return "The user closed the questions without answering.";
        var text = new StringBuilder();
        for (var i = 0; i < questions.Count; i++)
        {
            text.AppendLine($"Q: {OneLine(questions[i].Question)}");
            text.AppendLine($"A: {(answers[i].Count > 0 ? string.Join("; ", answers[i].Select(OneLine)) : "(no answer)")}");
        }
        return text.ToString().TrimEnd();
    }

    public static List<string> Answers(string result) => result.Split('\n').Where(line => line.StartsWith("A: "))
        .Select(line => line["A: ".Length..]).ToList();

    public static IReadOnlyList<AskQuestion> Parse(JsonNode arguments)
    {
        var questions = arguments.GetArray("questions").Select(item => new AskQuestion(
            item.GetString("question", ""),
            item.GetArray("options").Select(option => new AskOption(option.GetString("label", ""), option.GetString("description")))
                .ToList(),
            item.GetBool("multiSelect"))).ToList();
        if (questions.Count is 0 or > MaxQuestions)
            throw new ArgumentException($"questions must have 1 to {MaxQuestions} items");
        if (questions.Any(question => question.Question.Length == 0 || question.Options.Any(option => option.Label.Length == 0)))
            throw new ArgumentException("Every question needs its text and every option needs a label");
        if (questions.Any(question => question.Options.Count is < MinOptions or > MaxOptions))
            throw new ArgumentException($"Every question needs {MinOptions} to {MaxOptions} options");
        return questions;
    }

    private static string OneLine(string text) => text.ReplaceLineEndings(" ");
}
